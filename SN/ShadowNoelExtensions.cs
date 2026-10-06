using m2d;
using nel;
using nel.mgm.smncr;
using UnityEngine;
using PolarisNoels.DataStruct;
using XX;

namespace PolarisNoels.SN
{
    public static class ShadowNoelExtensions
    {
        /// <summary>
        /// 在当前地图为远程玩家生成影子角色。
        /// 玩家记录不存在时同时创建记录；记录已存在但角色未显示（刚进入同图）时只重新生成角色。
        /// </summary>
        public static ShadowNoel GenerateShadowNoel(ClientConfig config, int id = -1)
        {
            Plugin.Logger.LogInfo("generate");
            bool known = DB.noelIns.TryGetValue(id, out ShadowNoelInstance ins);
            if (known && ins.Enabled)
            {
                return null;
            }
            Map2d map = M2DBase.Instance.curMap;
            map.Pr.getPosition(out float x, out float y);
            ShadowNoel noel = map.createMover<ShadowNoel>("ShadowNoel", x, y);
            noel.InitConfig = config;
            noel.newGame();
            noel.gameObject.AddComponent<Rigidbody2D>();
            noel.gameObject.name = "ShadowNoel";
            map.assignMover(noel);
            noel.ID = id;
            noel.PartyID = known ? ins.NoelInfo.PartyID : DB.partyInfos[id].ID;
            EntityFactory.AttachPlayerReplica(noel, id);
            if (!known)
            {
                ins = new ShadowNoelInstance
                {
                    Noel = noel,
                    Nickname = config.Nickname,
                    MpKey = map.key,
                    NoelInitConfig = config,
                    NoelInfo = GetSendInfo(),
                    Enabled = true,
                    ID = id
                };
                DB.noelIns.Add(id, ins);
            }
            noel.CreateNicknameWithNoel(DB.InitConfig.InvisibleNickname
                ? TX.Get("multiplayer_noel_nickname") + id.ToString()
                : ins.NickNameStr);
            ins.NicknameIns = noel.NicknameIns;
            return noel;
        }

        public static void GenerateMainPRNickname(string nickname)
        {
            if (DB.MainPR.Mp.getMoverByName($"Nickname_{nickname}") is not null)
            {
                return;
            }
            DB.MainPRNickname = ShadowNoelNickname.CreateNickname(DB.MainPR, nickname);
            DB.MainPRMsg = ShadowNoelNickname.CreateMessageBubble(DB.MainPR);
        }

        public static void UpdateShadowNoelInfo(int id)
        {
            ShadowNoelInstance ins = DB.noelIns[id];
            UpdateNoelInfo info = ins.NoelInfo;
            ins.MpKey = PolarisNoelsTools.peer?.GetPeerMap(id) ?? info.MpKey;
            // 地图通知只更新可见性；等同图的新快照到达后再恢复角色，避免套用旧地图坐标。
            if (ins.MpKey != DB.MainPR.Mp.key || info.MpKey != ins.MpKey)
            {
                DisableShadowNoel(id);
                return;
            }
            else
            {
                EnableShadowNoel(id);
            }
            ShadowNoel noel = ins.Noel;
            MoveShadowNoel(noel, new(info.PositionX, info.PositionY));
            SetPoseShadowNoel(noel, info.Pose, (AIM)info.Aim);
            SetHPMP(noel, info.Hp, info.Mp);
            if (noel.getSkillManager().getCurrentCaneEquip().GetItem().id != info.CaneItemId)
            {
                SetCane(noel, (ushort)info.CaneItemId, (byte)info.CaneGrade);
            }
            if (noel.CurState != (PR.STATE)info.State)
            {
                noel.CurState = (PR.STATE)info.State;
            }
            noel.PartyID = info.PartyID;
            if (noel.PartyID != DB.LocalNoelParty && PolarisNoelsTools.EnablePVP)
            {
                EnableShadowNoelHit(noel);
            }
            else
            {
                DisableShadowNoelHit(noel);
            }
            noel.Magic.SyncFrom(info);
            noel.IsEvadeO = info.IsEvadeO;
            noel.Skill.ShE.evade_t = info.EvadeT;
            noel.IsAtkO = info.IsAtkO;
            noel.Skill.ShE.Shield.shiftx = info.ShieldShiftX;
            noel.Skill.ShE.Shield.shifty = info.ShieldShiftY;
            noel.Skill.ShE.Shield.scale = info.ShieldScale;
            noel.Skill.ShE.Shield.pow = info.ShieldPow;
            noel.CurShieldState = (M2Shield.STATE)info.ShieldState;
        }

        public static void DisableShadowNoel(int id)
        {
            if (!DB.noelIns.ContainsKey(id))
            {
                Plugin.Logger.LogWarning("try to disable not existing noel");
                return;
            }
            if (!DB.noelIns[id].Enabled)
            {
                return;
            }
            ShadowNoel noel = DB.noelIns[id].Noel;
            noel.Magic.Dispose();
            noel.Mp.destructPxlAnimByMover(noel);
            noel.Mp.removeMover(noel);
            noel.destruct();
            Object.DestroyImmediate(noel.gameObject);
            DB.noelIns[id].Enabled = false;
            DB.noelIns[id].Noel = null;
        }

        public static void EnableShadowNoel(int id)
        {
            if (!DB.noelIns.ContainsKey(id))
            {
                Plugin.Logger.LogWarning("try to disable not existing noel");
                return;
            }
            if (DB.noelIns[id].Enabled)
            {
                return;
            }
            ShadowNoel noel = GenerateShadowNoel(DB.noelIns[id].NoelInitConfig, id);
            DB.noelIns[id].Enabled = true;
            DB.noelIns[id].Noel = noel;
        }

        public static void MoveShadowNoel(ShadowNoel noel, System.Numerics.Vector2 pos)
        {
            if (noel.Phy is null)
            {
                return;
            }
            noel.setTo(pos.X, pos.Y);
            noel.Phy.killSpeedForce(true, true, true, true, true);
        }

        public static void SetPoseShadowNoel(ShadowNoel noel, string pose, AIM aim)
        {
            noel.setAim(aim);
            ShadowNoelAnimator Anm = (ShadowNoelAnimator)noel.Anm;
            if (Anm.pose_title == pose)
            {
                return;
            }
            Anm.setPose(pose);
        }

        public static void SetHPMP(ShadowNoel noel, int hp, int mp)
        {
            noel.hp = hp;
            noel.mp = mp;
        }

        public static void SetCane(ShadowNoel noel, ushort key, byte grade)
        {
            NelItem item = NelItem.GetByUId(key, true);
            if (item == null)
                return;
            CaneManager.CaneItem cane = CaneManager.Get(item, true);
            if (cane == null)
                return;
            if (noel.getSkillManager().getCurrentCaneEquip().cane_key == cane.key)
            {
                return;
            }
            noel.getSkillManager().switchCane(cane, grade, false);
        }

        /// <summary>把另一名玩家（副本）转来的伤害结算到本机玩家身上。</summary>
        public static void DamageLocalNoel(NotifyNoelDamage dmg)
        {
            DB.MainPR.DMG.applyDamage(new NelAttackInfo
            {
                attr = MGATTR.NORMAL,
                ndmg = NDMG.DEFAULT,
                hpdmg0 = dmg.Hp,
                mpdmg0 = dmg.Mp,
                fix_damage = true,
                parryable = false,
                shield_break_ratio = 1f,
                ignore_nodamage_time = true,
                nodamage_time = 0,
            }, true);
        }

        public static void StartCurMapBattle(string key, int starterID)
        {
            if (M2LpSummon.NearLpSmn is not null && M2LpSummon.NearLpSmn.key == key)
            {
                PolarisNoelsTools.BattleStarterID = starterID;
                DB.CurSummoner = M2LpSummon.NearLpSmn;
                DB.CurEnemies.Clear();
                M2LpSummon.NearLpSmn.openSummoner(DB.MainPR);
            }
            else
            {
                DB.StartedBattleSummonerKeys.Add(key);
            }
        }

        public static void StartSimBattle(int starterID, int x, int y)
        {
            if (PolarisNoelsTools.SimBattleReady)
            {
                SmncStageEditorManager.StgObject stg = PolarisNoelsTools.CurSimFile.Astgo[0];
                stg.x = x;
                stg.y = y;
                PolarisNoelsTools.CurSimFile.Astgo[0] = stg;
                PolarisNoelsTools.BattleStarterID = starterID;
                DB.CurEnemies.Clear();
                PolarisNoelsTools.OpenSmncBattle();
            }
        }

        public static void EndCurMapBattle()
        {
            if (DB.CurSummoner is not null)
            {
                foreach (NelEnemy enemy in DB.CurEnemies)
                {
                    if (enemy == null)
                    {
                        continue;
                    }
                    DB.MainPR.Mp.removeMover(enemy);
                    enemy.destruct();
                }
                DB.CurEnemies.Clear();
                DB.CurSummoner.closeSummoner(true, out _);
                DB.CurSummoner = null;
            }
        }

        public static void DisableShadowNoelHit(ShadowNoel noel)
        {
            noel.gameObject.tag = "MoverPr";
            noel.gameObject.layer = 0; //Default
        }

        public static void EnableShadowNoelHit(ShadowNoel noel)
        {
            noel.gameObject.tag = "MoverEn";
            noel.gameObject.layer = 23; //23 is EmenyLayer
        }

        public static bool IsNearLpSummon(this ShadowNoel noel, M2LpSummon summon)
        {
            bool is_quest_rescue = summon.is_quest_rescue;
            float mapfocx = summon.mapfocx;
            float num = X.Mn((float)(summon.mapy + summon.maph - 3), summon.mapfocy);
            float num2 = (is_quest_rescue ? ((float)(summon.mapy + 2)) : (summon.mapfocy - 0.5f));
            bool flag2;
            if (summon.is_sudden == M2LpSummon.SUDDEN.NORMAL)
            {
                flag2 = X.BTW(mapfocx - 3.3f, noel.x, mapfocx + 3.3f) && !summon.nM2D.NightCon.isUiActive();
            }
            else
            {
                flag2 = (noel.vx != 0f || noel.vy != 0f) && X.BTW((float)summon.mapx + summon.sudden_margin_x, noel.x, (float)(summon.mapx + summon.mapw) - summon.sudden_margin_x);
            }
            if (flag2 && X.BTW(num2, noel.y, (float)(summon.mapy + summon.maph) + 0.5f))
            {
                float num3 = X.Mn((float)(summon.mapy + summon.maph), summon.Mp.getFootableY(summon.mapfocx, (int)summon.mapfocy, 14, false, -1f, false, true, true, 0f)) + 0.5f;
                flag2 = X.BTW(num, noel.mbottom, num3);
            }
            else
            {
                flag2 = false;
            }
            return flag2;
        }

        public static UpdateNoelInfo GetSendInfo()
        {
            DB.MainPR.getPosition(out float x, out float y);
            PrNoelAnimator Anm = DB.MainPR.AnmN;
            M2PrSkill skill = DB.MainPR.getSkillManager();
            MagicItem item = skill.CurMg;
            PrCaneEquip cane = skill.getCurrentCaneEquip();
            return new()
            {
                PositionX = x,
                PositionY = y,
                Pose = Anm.pose_title,
                Aim = Anm.pose_aim,
                IsCrouch = DB.MainPR.is_crouch,
                Hp = DB.MainPR.hp,
                Mp = DB.MainPR.mp,
                State = (int)DB.MainPR.state,
                CaneItemId = cane.GetItem().id,
                CaneGrade = cane.grade,
                PartyID = DB.LocalNoelParty,
                MpKey = DB.MainPR.Mp.key,
                ChantMagic = DB.MainPR.magic_chanting,
                MagicAgR = item is null ? 0 : item.aim_agR,
                MagicHold = skill.mp_hold,
                MagicT = skill.magic_t,
                MagicHoldAim = skill.Cursor.pre_hold_aim,
                IsEvadeO = DB.MainPR.isEvadeO(),
                EvadeT = DB.MainPR.Skill.ShE.evade_t,
                IsAtkO = DB.MainPR.isAtkO(),
                ShieldShiftX = DB.MainPR.Skill.ShE.Shield.shiftx,
                ShieldShiftY = DB.MainPR.Skill.ShE.Shield.shifty,
                ShieldScale = DB.MainPR.Skill.ShE.Shield.scale,
                ShieldPow = DB.MainPR.Skill.ShE.Shield.pow,
                ShieldState = (int)DB.MainPR.Skill.ShE.Shield.stt,
                HoldT = DB.MainPR.Skill.Cursor.t_hold
            };
        }
    }
}
