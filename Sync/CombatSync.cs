using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using m2d;
using nel;
using PolarisNoels.DataStruct;
using PolarisNoels.Networking;
using PolarisNoels.SN;
using UnityEngine;

namespace PolarisNoels
{
    /// <summary>Hits originate at the source owner; only the target owner executes damage.</summary>
    public static class CombatSync
    {
        public static string LocalEpoch { get; private set; } = Guid.NewGuid().ToString("N");
        static long nextId;
        static readonly CombatLedger ledger = new();
        static readonly Dictionary<int, string> epochs = new(), lives = new();
        static readonly Dictionary<int, EntityState> states = new();
        static readonly Dictionary<M2Mover, List<CombatLedger.Bounds>> history = new();
        static readonly Dictionary<string, World> world = new();
        static readonly Dictionary<long, Pending> pending = new();
        static readonly List<Scope> scopes = new();
        static int resolving;
        static bool sourceParried;
        static int parryAim;
        static float nextWorldUpdate;
        static double Now => Time.realtimeSinceStartup;
        static int Local => PolarisNoelsTools.LocalID;
        static PolarisNoelsPeer Peer => PolarisNoelsTools.peer;
        static string Map => DB.MainPR?.Mp?.key;
        static string Visit => Peer?.LocalMapVisit;

        sealed class World
        {
            public M2Mover Mover;
            public string Life;
            public ulong Revision;
            public double Time;
        }
        sealed class Pending
        {
            public CombatRequest Request;
            public M2Mover Target;
            public AttackInfo Attack;
            public M2Mover Source;
            public MagicItem Magic;
            public int MagicId;
            public double Sent;
            public int Owner;
        }
        public sealed class Scope
        {
            internal M2Mover Target;
            internal NetEntity Entity;
            internal AttackInfo Attack;
            internal int Hp, Mp, Return, Hit;
            internal bool Publish;
        }

        static string Key(M2Mover mover) => mover.GetType().FullName + "|" + mover.key;
        static int Hp(M2Mover mover) => mover is M2Attackable attackable ? (int)attackable.get_hp()
            : mover is M2BreakableWallMover wall ? wall.get_hp() : 0;
        static int Mp(M2Mover mover) => mover is M2Attackable attackable ? (int)attackable.get_mp() : 0;
        static bool Supported(M2Mover mover) => mover is M2Attackable || mover is M2BreakableWallMover;

        static NetEntity Entity(M2Mover mover, out int[] path)
        {
            var indexes = new List<int>();
            for (M2Mover current = mover; current != null;)
            {
                if (current.TryGetComponent<NetEntity>(out var entity))
                { indexes.Reverse(); path = indexes.ToArray(); return entity; }
                if (current is not NelEnemyNested part || part.Parent == null || part.Parent == current) break;
                int index = part.Parent.ANested?.IndexOf(part) ?? -1;
                if (index < 0 || indexes.Count >= 16) break;
                indexes.Add(index);
                current = part.Parent;
            }
            path = Array.Empty<int>();
            return null;
        }
        static M2Mover Part(M2Mover root, int[] path)
        {
            if (path == null) return root;
            if (path.Length > 16) return null;
            foreach (int index in path)
            {
                if (root is not NelEnemy enemy || enemy.ANested == null || index < 0 || index >= enemy.ANested.Count) return null;
                root = enemy.ANested[index];
            }
            return root;
        }
        static int Owner(M2Mover target, NetEntity entity) => entity != null ? entity.IsAuthority ? Local : entity.OwnerPeer
            : target is PR && target is not ShadowNoel ? Local : 0;
        static M2Mover Source(AttackInfo attack)
        {
            var caster = (attack as NelAttackInfo)?.Caster ?? (attack as NelAttackInfo)?.PublishMagic?.Caster;
            return caster as M2Mover ?? (caster as M2ShieldHitable)?.Sld?.Mv ?? attack?.AttackFrom;
        }
        static World RegisterWorld(M2Mover mover)
        {
            string key = Key(mover);
            if (!world.TryGetValue(key, out var value) || value.Mover != mover)
                world[key] = value = new World { Mover = mover, Life = Local == 0 ? Guid.NewGuid().ToString("N") : null };
            return value;
        }
        public static bool Owns(M2Mover mover)
        {
            if (!DB.IsMultiplayer) return true;
            var entity = Entity(mover, out _);
            return entity != null ? entity.IsAuthority : mover is PR ? mover is not ShadowNoel : Local == 0;
        }

        // Harmony enters here at the outer attack entry and at raw HP entry points.
        public static bool Before(M2Mover target, MethodBase method, object[] args, out Scope scope, CombatMode? mode = null)
        {
            scope = null;
            if (!DB.IsMultiplayer || Peer == null || Local < 0 || !Supported(target)) return true;
            if (scopes.Any(active => active.Target == target)) return Owns(target);
            if (method.Name != "applyDamage" && method.Name != "applyAbsorbDamage" && method.Name != "applyHpDamage" && method.Name != "applyHpDamageSimple" && method.Name != "applyMpDamage")
                return Owns(target);
            var attack = args.OfType<AttackInfo>().FirstOrDefault();
            var source = Source(attack);
            var entity = Entity(target, out var targetPath);
            int owner = Owner(target, entity);
            // During an accepted request original nested/parent transfers may run on owned targets.
            if (resolving > 0)
            {
                if (!Owns(target)) return false;
                scope = BeginScope(target, entity, attack); return true;
            }
            var sourceEntity = source != null ? Entity(source, out _) : null;
            // Replica projectiles are presentation only. Their source owner supplies the request.
            if (source != null && !Owns(source)) return false;
            if (owner == Local && (entity == null || entity.IsAuthority))
            { scope = BeginScope(target, entity, attack); return true; }
            if (source == null || attack == null || entity != null && (entity.Id < 0 || string.IsNullOrEmpty(entity.Life))) return false;
            if (pending.Count >= 512 || pending.Values.Any(item => item.Target == target && ReferenceEquals(item.Attack, attack))) return false;
            var worldTarget = entity == null ? RegisterWorld(target) : null;
            string life = entity?.Life ?? worldTarget?.Life;
            string targetVisit = Peer.GetPeerMapVisit(owner);
            if (string.IsNullOrEmpty(life) || string.IsNullOrEmpty(targetVisit) || string.IsNullOrEmpty(Visit)) return false;
            int[] sourcePath = Array.Empty<int>();
            if (source != null) sourceEntity = Entity(source, out sourcePath);
            if (sourceEntity != null && (sourceEntity.Id < 0 || string.IsNullOrEmpty(sourceEntity.Life))) return false;
            var request = new CombatRequest
            {
                Id = ++nextId, SenderEpoch = LocalEpoch,
                TargetId = entity?.Id ?? -1, TargetKey = target.key, TargetType = target.GetType().FullName,
                TargetLife = life, TargetPath = targetPath,
                SourceId = sourceEntity?.Id ?? -1, SourceKey = source.key, SourceType = source.GetType().FullName,
                SourceLife = sourceEntity?.Life ?? RegisterWorld(source).Life, SourcePath = sourcePath,
                MapKey = target.Mp?.key, SourceVisit = Visit, TargetVisit = targetVisit,
                ObservedTime = entity?.SampleTime ?? worldTarget.Time,
                Attack = CombatAttackCodec.Capture(attack),
                Mode = mode ?? (method.Name == "applyAbsorbDamage" ? CombatMode.Absorb
                    : method.Name == "applyHpDamageSimple" ? CombatMode.PlayerSimple
                    : method.Name == "applyMpDamage" ? CombatMode.Mp
                    : method.Name == "applyHpDamage" ? method.GetParameters().Any(parameter => parameter.ParameterType == typeof(int).MakeByRefType())
                        ? CombatMode.EnemyHpMp : CombatMode.Hp : CombatMode.Attack)
            };
            if (method.Name == "applyHpDamage" && args.Length > 0 && args[0] is int rawHp) request.Value = rawHp;
            var parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                switch (parameters[i].Name)
                {
                    case "force": if (!parameters[i].IsOut) request.Force = (bool)args[i]; break;
                    case "val": request.Value = (int)args[i]; break;
                    case "mpdmg": request.MpValue = (int)args[i]; break;
                    case "show_damage_counter": request.ShowCounter = (bool)args[i]; break;
                    case "use_quake": request.UseQuake = (bool)args[i]; break;
                    case "calc_gsaver": request.CalcGsaver = (bool)args[i]; break;
                    case "do_not_reduce_gsaver": request.KeepGsaver = (bool)args[i]; break;
                    case "use_cusion": request.UseCushion = (bool)args[i]; break;
                    case "execute_attack": request.Execute = (bool)args[i]; break;
                    case "mouth_damage": request.Mouth = (bool)args[i]; break;
                    case "fade_key": request.FadeKey = args[i] as string; break;
                    case "decline_ui_additional_effect":
                    case "decline_additional_effect": request.DeclineEffects = (bool)args[i]; break;
                    case "from_press_damage": request.FromPress = (bool)args[i]; break;
                }
            }
            var magic = (attack as NelAttackInfo)?.PublishMagic;
            pending[request.Id] = new Pending { Request = request, Target = target, Source = source, Attack = attack,
                Magic = magic, MagicId = magic?.id ?? -1, Sent = Now, Owner = owner };
            Send(owner, request.TargetId, new EntityEvent { Type = EntityEventType.DamageRequest, Request = request });
            return false;

        }
        static Scope BeginScope(M2Mover target, NetEntity entity, AttackInfo attack)
        {
            var scope = new Scope { Target = target, Entity = entity, Attack = attack, Hp = Hp(target), Mp = Mp(target), Publish = resolving == 0 };
            scopes.Add(scope);
            return scope;
        }

        public static void Returned(Scope scope, int value, object[] args)
        {
            if (scope == null) return;
            scope.Return = value;
            foreach (var arg in args) if (arg is HITTYPE hit) scope.Hit = (int)hit;
        }
        public static Exception Finish(Scope scope, Exception error)
        {
            if (scope == null) return error;
            scopes.Remove(scope);
            if (scope.Publish && scope.Target != null)
            {
                // Also publish zero damage: shields/parries are authoritative outcomes.
                var result = Capture(scope.Target, scope.Entity, null, Local, scope.Hp, scope.Mp,
                    error == null ? CombatOutcome.Applied : CombatOutcome.Invalid, scope.Return, scope.Hit, scope.Attack);
                Publish(result);
            }
            return error;
        }

        static CombatResult Capture(M2Mover target, NetEntity entity, CombatRequest request, int requester,
            int beforeHp, int beforeMp, CombatOutcome outcome, int returned, int hit, AttackInfo attack)
        {
            var state = entity != null ? new EntityState() : null;
            if (state != null) entity.WriteState(state);
            var prop = entity == null ? RegisterWorld(target) : null;
            if (prop != null) { prop.Revision++; prop.Time = Now; }
            int hp = Hp(target), mp = Mp(target);
            if (outcome == CombatOutcome.Applied && hp == beforeHp && mp == beforeMp && returned == 0) outcome = CombatOutcome.NoDamage;
            return new CombatResult
            {
                RequestId = request?.Id ?? 0, Requester = requester, SenderEpoch = request?.SenderEpoch,
                TargetId = entity?.Id ?? -1, TargetKey = request?.TargetKey ?? target.key, TargetType = request?.TargetType ?? target.GetType().FullName,
                MapKey = target.Mp?.key, TargetVisit = Visit, Life = entity?.Life ?? prop.Life,
                Revision = entity?.Revision ?? prop.Revision, SampleTime = entity?.SampleTime ?? prop.Time,
                Outcome = outcome, Hp = hp, Mp = mp, HpDelta = hp - beforeHp, MpDelta = mp - beforeMp,
                ReturnValue = returned, HitType = hit, Snapshot = state,
                MagicKilled = (attack as NelAttackInfo)?.PublishMagic?.killed == true, X = target.x, Y = target.y,
                ReflectionHit = (int)(((attack as NelAttackInfo)?.PublishMagic?.Ray?.hittype ?? HITTYPE.NONE) & HITTYPE._TEMP_REFLECT),
                Reflector = CombatAttackCodec.CaptureRay((attack as NelAttackInfo)?.PublishMagic?.Ray?.ReflectAnotherRay)
            };
        }
        static CombatResult Rejected(int sender, CombatRequest request, CombatOutcome outcome) => new()
        {
            RequestId = request.Id, Requester = sender, SenderEpoch = request.SenderEpoch,
            TargetId = request.TargetId, TargetKey = request.TargetKey, TargetType = request.TargetType,
            MapKey = request.MapKey, TargetVisit = request.TargetVisit, Life = request.TargetLife, Outcome = outcome
        };
        static void Send(int peer, int id, EntityEvent ev)
        {
            Peer?.SendToPeer(peer, new PolarisNoelsPeerMessage { Type = PolarisNoelsPeerMessageType.Entity,
                PeerId = Local, MapKey = Map, Entity = new EntityMessage { Type = EntityMsgType.Event, EntityId = id, Event = ev } }, NetChannel.Reliable);
        }
        static void Publish(CombatResult result)
        {
            if (Peer == null) return;
            foreach (int peer in NetworkRuntime.ConnectedPeers)
                if (Peer.IsPeerOnCurrentMap(peer) || result.RequestId > 0 && peer == result.Requester)
                    Send(peer, result.TargetId, new EntityEvent { Type = EntityEventType.DamageResult, Result = result });
        }
        static M2Mover Resolve(int id, string key, string type, string life, int[] path, out NetEntity entity)
        {
            entity = null;
            M2Mover mover;
            if (id >= 0)
            {
                if (!EntityRegistry.TryGet(id, out entity) || entity.Life != life) return null;
                mover = Part(entity.GetComponent<M2Mover>(), path);
                // Player replicas have the ShadowNoel type, while the owner's object is PRNoel.
                if (EntityIds.IsPlayer(id)) return mover is PR ? mover : null;
                // Replicated ordinary enemies intentionally have different mover keys.
                return mover != null && mover.GetType().FullName == type ? mover : null;
            }
            mover = DB.MainPR?.Mp?.getMoverByName(key, true);
            if (mover == null || mover.GetType().FullName != type || Entity(mover, out _) != null) return null;
            return RegisterWorld(mover).Life == life ? mover : null;
        }

        public static void ReceiveRequest(int sender, int envelopeId, CombatRequest request)
        {
            if (!DB.IsMultiplayer || Peer == null || request == null || envelopeId != request.TargetId
                || !epochs.TryGetValue(sender, out string epoch) || epoch != request.SenderEpoch) return;
            if ((request.TargetId >= 0 ? EntityIds.Owner(request.TargetId) : 0) != Local) return;
            if (!ledger.Begin(sender, epoch, request.Id, out var cached))
            { if (cached != null) Send(sender, envelopeId, new EntityEvent { Type = EntityEventType.DamageResult, Result = cached }); return; }
            var result = Rejected(sender, request, CombatOutcome.Invalid);
            NelAttackInfo attack = null;
            M2Mover affected = null;
            NetEntity affectedEntity = null;
            int beforeHp = 0, beforeMp = 0;
            try
            {
                if (!CombatAttackCodec.IsValid(request.Attack) || !Enum.IsDefined(typeof(CombatMode), request.Mode)
                    || request.FadeKey?.Length > 256 || request.TargetKey?.Length > 512 || request.SourceKey?.Length > 512
                    || request.TargetType?.Length > 256 || request.SourceType?.Length > 256) return;
                if (request.MapKey != Map || request.TargetVisit != Visit
                    || request.SourceVisit != Peer.GetPeerMapVisit(sender) || !Peer.IsPeerOnCurrentMap(sender))
                { result.Outcome = CombatOutcome.Unavailable; return; }
                if ((request.SourceId >= 0 ? EntityIds.Owner(request.SourceId) : 0) != sender) return;
                var target = Resolve(request.TargetId, request.TargetKey, request.TargetType, request.TargetLife, request.TargetPath, out var entity);
                var source = Resolve(request.SourceId, request.SourceKey, request.SourceType, request.SourceLife, request.SourcePath, out _);
                if (target == null || source == null || !Owns(target) || !Supported(target))
                { result.Outcome = CombatOutcome.Unavailable; return; }
                if (target is PR && source is PR && (!PolarisNoelsTools.EnablePVP
                    || source is ShadowNoel shadow && shadow.PartyID == DB.LocalNoelParty)) return;
                history.TryGetValue(target, out var samples);
                int rtt = DB.peerDelays.TryGetValue(sender, out var delay) ? delay : 0;
                var outcome = CombatLedger.ValidateHit(Now, request.ObservedTime, rtt, request.Attack.HitX, request.Attack.HitY,
                    Bounds(target), samples, request.Attack.Radius);
                int hp = Hp(target), mp = Mp(target), returned = 0;
                HITTYPE hit = HITTYPE.NONE;
                if (outcome == CombatOutcome.Applied)
                {
                    attack = CombatAttackCodec.Restore(request.Attack, source);
                    affected = target; affectedEntity = entity; beforeHp = hp; beforeMp = mp;
                    sourceParried = false; parryAim = 0;
                    resolving++;
                    try
                    {
                        if (attack.ignore_nodamage_time && target is M2Attackable defended) defended.penetrateNoDamageTime(attack.ndmg);
                        switch (request.Mode)
                        {
                            case CombatMode.Attack:
                                if (target is NelEnemy enemy) returned = enemy.applyDamage(attack, ref hit, request.Force);
                                else if (target is PR player) returned = player.DMG.applyDamage(attack, ref hit, request.Force,
                                    request.FadeKey, request.DeclineEffects, request.FromPress);
                                else outcome = CombatOutcome.Invalid;
                                break;
                            case CombatMode.Absorb:
                                if (target is PR absorbed) absorbed.applyAbsorbDamage(attack, request.Execute, request.Mouth, request.FadeKey, request.DeclineEffects);
                                else outcome = CombatOutcome.Invalid;
                                break;
                            case CombatMode.Hp:
                                returned = ((IM2RayHitAble)target).applyHpDamage(request.Value, request.Force, attack);
                                break;
                            case CombatMode.EnemyHpMp:
                                if (target is NelEnemy hpEnemy)
                                {
                                    int mpDamage = request.MpValue;
                                    returned = hpEnemy.applyHpDamage(request.Value, ref mpDamage, request.Force, attack);
                                }
                                else outcome = CombatOutcome.Invalid;
                                break;
                            case CombatMode.PlayerSimple:
                                if (target is PR simplePlayer)
                                    returned = simplePlayer.DMG.applyHpDamageSimple(attack, out _, request.Value, request.ShowCounter);
                                else outcome = CombatOutcome.Invalid;
                                break;
                            case CombatMode.Shield:
                                if (target is PR shieldPlayer && shieldPlayer.Skill.ShE.Shield.Hitable != null)
                                    returned = shieldPlayer.Skill.ShE.Shield.Hitable.applyHpDamage(request.Value, request.Force, attack);
                                else outcome = CombatOutcome.NoDamage;
                                break;
                            case CombatMode.Mp:
                                if (target is PR mpPlayer) returned = mpPlayer.applyMpDamage(out _, request.Value, request.Force, attack,
                                    request.UseQuake, request.CalcGsaver, request.KeepGsaver, request.UseCushion);
                                else if (target is M2Attackable mpTarget) returned = mpTarget.applyMpDamage(request.Value, request.Force, attack);
                                else outcome = CombatOutcome.Invalid;
                                break;
                        }
                    }
                    finally { resolving--; }
                }
                result = Capture(target, entity, request, sender, hp, mp, outcome, returned, (int)hit, attack);
                result.SourceParried = attack != null && sourceParried;
                result.ParryAim = parryAim;
                // Nested damage can transfer HP to another registered owner entity.
                foreach (var owned in EntityRegistry.GetAuthorities())
                    if (owned != entity && owned.GetComponent<NelEnemy>() is NelEnemy parent
                        && target is NelEnemyNested nested && IsAncestor(parent, nested))
                    {
                        var parentResult = Capture(parent, owned, null, sender, Hp(parent), Mp(parent), CombatOutcome.Applied, 0, 0, null);
                        Publish(parentResult);
                    }
            }
            catch (Exception error)
            {
                Plugin.Logger.LogError($"damage authority failed: {error}");
                // Publish actual state even if original code throws after changing HP.
                if (affected != null) result = Capture(affected, affectedEntity, request, sender, beforeHp, beforeMp, CombatOutcome.Invalid, 0, 0, attack);
            }
            finally
            {
                CombatAttackCodec.ReleaseContext(attack?.PublishMagic);
                ledger.Complete(sender, epoch, request.Id, result);
                Publish(result);
            }
        }
        static bool IsAncestor(NelEnemy parent, NelEnemyNested child)
        {
            for (NelEnemy current = child.Parent; current != null;)
            {
                if (current == parent) return true;
                if (current is not NelEnemyNested part || part.Parent == current) return false;
                current = part.Parent;
            }
            return false;
        }

        public static void ReceiveResult(int sender, int envelopeId, CombatResult result)
        {
            if (result == null || envelopeId != result.TargetId || Peer == null
                || (result.TargetId >= 0 ? EntityIds.Owner(result.TargetId) : 0) != sender) return;
            if (result.RequestId > 0 && result.Requester == Local && result.SenderEpoch == LocalEpoch
                && pending.TryGetValue(result.RequestId, out var ticket) && ticket.Owner == sender
                && ticket.Request.TargetId == result.TargetId && ticket.Request.TargetLife == result.Life
                && ticket.Request.TargetKey == result.TargetKey && ticket.Request.TargetVisit == result.TargetVisit)
            {
                pending.Remove(result.RequestId);
                if (result.MagicKilled && ticket.Magic != null && ticket.Magic.id == ticket.MagicId && !ticket.Magic.killed) ticket.Magic.kill(-1);
                else if (ticket.Magic?.Ray != null && ticket.Magic.id == ticket.MagicId && !ticket.Magic.killed
                    && CombatAttackCodec.ValidRay(result.Reflector))
                {
                    var ray = ticket.Magic.Ray;
                    ray.hittype |= (HITTYPE)result.ReflectionHit & HITTYPE._TEMP_REFLECT;
                    if (result.Reflector != null)
                    {
                        // A detached context survives until native reflectVS consumes it.
                        var reflector = new M2Ray(null).Set(DB.MainPR.Mp, ticket.Target);
                        CombatAttackCodec.RestoreRay(reflector, result.Reflector);
                        ray.ReflectAnotherRay = reflector;
                    }
                }
                if (result.SourceParried && ticket.Source is NelEnemy parried && Owns(parried)
                    && (Entity(parried, out _)?.Life ?? RegisterWorld(parried).Life) == ticket.Request.SourceLife)
                    parried.applyParry((XX.AIM)result.ParryAim);
                if (DB.ShowReceiveDebug) Plugin.Logger.LogInfo($"hit {result.RequestId}: {result.Outcome}, HP {result.HpDelta}, MP {result.MpDelta}");
            }
            if (result.MapKey != Map || result.TargetVisit != Peer.GetPeerMapVisit(sender) || !Peer.IsPeerOnCurrentMap(sender)
                || result.Revision == 0) return;
            if (result.TargetId >= 0)
            {
                if (result.Snapshot == null || result.Snapshot.Life != result.Life || result.Snapshot.Revision != result.Revision) return;
                if (EntityRegistry.TryGet(result.TargetId, out var entity))
                { if (!entity.IsAuthority) entity.ReadState(result.Snapshot); }
                else if (CacheState(result.TargetId, result.Snapshot) && result.Snapshot.Noel != null)
                    PolarisNoelsTools.UpdateNoel(EntityIds.Owner(result.TargetId), result.Snapshot.Noel);
                return;
            }
            var mover = DB.MainPR?.Mp?.getMoverByName(result.TargetKey, true);
            if (mover == null || !Supported(mover) || mover.GetType().FullName != result.TargetType || Entity(mover, out _) != null) return;
            var prop = RegisterWorld(mover);
            if (prop.Life != result.Life)
            {
                if (result.SampleTime <= prop.Time) return;
                prop.Life = result.Life; prop.Revision = 0;
            }
            if (result.Revision <= prop.Revision) return;
            prop.Revision = result.Revision; prop.Time = result.SampleTime;
            if (mover is M2BreakableWallMover wall) wall.revertPuzzRevertHp(result.Hp, true);
            else if (mover is M2Attackable attackable)
            {
                bool died = attackable.hp > 0 && result.Hp <= 0;
                attackable.hp = result.Hp; attackable.mp = result.Mp;
                if (died && mover is M2MoverBarricadeTD) attackable.initDeath();
            }
        }

        static CombatLedger.Bounds Bounds(M2Mover mover) => new(Now, mover.x, mover.y, mover.sizex, mover.sizey);
        static void RecordMover(M2Mover mover)
        {
            if (mover == null) return;
            if (!history.TryGetValue(mover, out var samples)) history[mover] = samples = new List<CombatLedger.Bounds>();
            if (samples.Count > 0 && Now - samples[samples.Count - 1].Time < 0.03) return;
            samples.Add(Bounds(mover));
            samples.RemoveAll(sample => Now - sample.Time > 3);
            if (samples.Count > 128) samples.RemoveRange(0, samples.Count - 128);
            if (mover is NelEnemy enemy && enemy.ANested != null)
                foreach (var child in enemy.ANested) RecordMover(child);
        }
        public static void Record(NetEntity entity) => RecordMover(entity.GetComponent<M2Mover>());
        public static void Forget(NetEntity entity) => history.Remove(entity.GetComponent<M2Mover>());
        public static void ForgetSpawn(int id) { states.Remove(id); lives[id] = null; }
        public static bool BeforeParry(NelEnemy enemy, XX.AIM aim)
        {
            if (resolving > 0 && !Owns(enemy)) { sourceParried = true; parryAim = (int)aim; return false; }
            return true;
        }
        public static string GetReplicaLife(int id) => lives.TryGetValue(id, out string life) ? life : null;
        public static bool AcceptSpawn(int id, int sender, EntitySpawn spawn)
        {
            if (spawn == null || string.IsNullOrEmpty(spawn.Life) || spawn.Life.Length > 64
                || string.IsNullOrEmpty(spawn.CombatEpoch) || spawn.CombatEpoch.Length > 64) return false;
            if (spawn.Kind == EntityKind.Noel)
            {
                if (id != EntityIds.ForPlayer(sender)) return false;
                if (epochs.TryGetValue(sender, out var old) && old != spawn.CombatEpoch) RemovePeer(sender);
                epochs[sender] = spawn.CombatEpoch;
            }
            lives[id] = spawn.Life;
            if (lives.Count > 4096)
                foreach (int oldId in lives.Keys.Where(value => value != id && !EntityRegistry.TryGet(value, out _)).Take(1024).ToArray())
                { lives.Remove(oldId); states.Remove(oldId); }
            if (EntityRegistry.TryGet(id, out var entity)) entity.SetReplicaLife(spawn.Life);
            return true;
        }
        public static bool CacheState(int id, EntityState state)
        {
            if (state == null || state.Revision == 0 || string.IsNullOrEmpty(state.Life) || state.Life.Length > 64) return false;
            if (lives.TryGetValue(id, out var life) && state.Life != life) return false;
            if (states.TryGetValue(id, out var old) && old.Life == state.Life && old.Revision >= state.Revision) return false;
            if (!states.ContainsKey(id) && states.Count >= 512) return false;
            states[id] = state;
            return true;
        }
        public static void ApplyPendingState(NetEntity entity)
        {
            if (entity == null || entity.IsAuthority) return;
            entity.SetReplicaLife(GetReplicaLife(entity.Id));
            if (states.TryGetValue(entity.Id, out var state)) entity.ReadState(state);
        }

        public static List<CombatPart> CaptureParts(NelEnemy root)
        {
            if (root == null) return null;
            var parts = new List<CombatPart>();
            void Walk(NelEnemy enemy, int[] path)
            {
                if (enemy.ANested == null || path.Length >= 16) return;
                for (int i = 0; i < enemy.ANested.Count; i++)
                {
                    var child = enemy.ANested[i];
                    if (child == null || child.TryGetComponent<NetEntity>(out _)) continue;
                    int[] next = path.Concat(new[] { i }).ToArray();
                    parts.Add(new CombatPart { Path = next, Type = child.GetType().FullName, Hp = child.hp, Mp = child.mp });
                    Walk(child, next);
                }
            }
            Walk(root, Array.Empty<int>());
            return parts.Count > 0 ? parts : null;
        }
        public static void ApplyParts(NelEnemy root, List<CombatPart> parts)
        {
            if (root == null || parts == null || parts.Count > 256) return;
            foreach (var value in parts)
                if (Part(root, value.Path) is NelEnemy child && child != root && child.GetType().FullName == value.Type
                    && !child.TryGetComponent<NetEntity>(out _)) { child.hp = value.Hp; child.mp = value.Mp; }
        }

        public static void Update()
        {
            foreach (var pair in pending.ToArray())
                if (Now - pair.Value.Sent > 5)
                {
                    pending.Remove(pair.Key);
                    Plugin.Logger.LogWarning($"hit {pair.Key} timed out; target owner {pair.Value.Owner} supplied no result");
                }
            foreach (var mover in history.Keys.Where(mover => mover == null || mover.Mp?.key != Map).ToArray()) history.Remove(mover);
            if (Time.realtimeSinceStartup < nextWorldUpdate || DB.MainPR?.Mp == null) return;
            nextWorldUpdate = Time.realtimeSinceStartup + 0.1f;
            // Discover after map initialization as well as after connection/map changes.
            foreach (var mover in DB.MainPR.Mp.getVectorMover())
            {
                if (mover == null || !Supported(mover) || mover is PR || Entity(mover, out _) != null) continue;
                var prop = RegisterWorld(mover);
                if (Local != 0) continue;
                RecordMover(mover);
                Publish(Capture(mover, null, null, Local, Hp(mover), Mp(mover), CombatOutcome.NoDamage, 0, 0, null));
            }
            foreach (string key in world.Where(pair => pair.Value.Mover == null || pair.Value.Mover.Mp?.key != Map).Select(pair => pair.Key).ToArray()) world.Remove(key);
        }
        public static void OnLocalMapChanged() { pending.Clear(); world.Clear(); history.Clear(); nextWorldUpdate = 0; }
        public static void RemovePeer(int peer)
        {
            epochs.Remove(peer); ledger.RemovePeer(peer);
            foreach (int id in lives.Keys.Where(id => EntityIds.Owner(id) == peer).ToArray()) { lives.Remove(id); states.Remove(id); }
            foreach (long id in pending.Where(pair => pair.Value.Owner == peer).Select(pair => pair.Key).ToArray()) pending.Remove(id);
            if (peer == 0) world.Clear();
        }
        public static void Reset()
        {
            ledger.Clear(); epochs.Clear(); lives.Clear(); states.Clear(); history.Clear(); world.Clear(); pending.Clear(); scopes.Clear();
            LocalEpoch = Guid.NewGuid().ToString("N"); nextId = 0; resolving = 0; sourceParried = false; nextWorldUpdate = 0;
        }
    }
}
