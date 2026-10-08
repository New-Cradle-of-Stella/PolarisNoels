using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using m2d;
using nel;
using PolarisNoels.DataStruct;
using UnityEngine;
using XX;

namespace PolarisNoels
{
    public static class CombatAttackCodec
    {
        static readonly Dictionary<Type, FieldInfo[]> fields = new();
        static readonly FieldInfo baseRadius = typeof(AttackInfo).GetField("hit_r_", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        static FieldInfo[] Fields(Type type)
        {
            if (!fields.TryGetValue(type, out var result))
                fields[type] = result = type.GetFields(BindingFlags.Instance | BindingFlags.Public)
                    .Where(field => !field.IsInitOnly && (field.FieldType.IsEnum || field.FieldType == typeof(bool)
                        || field.FieldType == typeof(byte) || field.FieldType == typeof(int)
                        || field.FieldType == typeof(uint) || field.FieldType == typeof(float)
                        || field.FieldType == typeof(double))).ToArray();
            return result;
        }
        static Dictionary<string, double> Capture(object value, Type type)
        {
            if (value == null) return null;
            var result = new Dictionary<string, double>();
            foreach (var field in Fields(type)) result[field.Name] = Convert.ToDouble(field.GetValue(value));
            return result;
        }
        static void Restore(object target, Type type, Dictionary<string, double> values)
        {
            if (values == null) return;
            foreach (var field in Fields(type))
            {
                if (!values.TryGetValue(field.Name, out double value)) continue;
                field.SetValue(target, field.FieldType.IsEnum ? Enum.ToObject(field.FieldType, (int)value)
                    : field.FieldType == typeof(bool) ? (object)(value != 0) : Convert.ChangeType(value, field.FieldType));
            }
        }

        public static CombatAttack Capture(AttackInfo attack)
        {
            var nel = attack as NelAttackInfo;
            var core = attack as NelAttackInfoBase;
            var result = new CombatAttack
            {
                Values = Capture(attack, nel != null ? typeof(NelAttackInfo) : core != null ? typeof(NelAttackInfoBase) : typeof(AttackInfo)),
                CenterX = attack.center_x, CenterY = attack.center_y,
                HitX = attack.hit_x, HitY = attack.hit_y,
                Radius = baseRadius != null ? Convert.ToSingle(baseRadius.GetValue(attack)) : nel?.hit_r ?? 0,
                HitEffect = attack.hit_ptcst_name, Sound = attack.snd_name,
                Ray = CaptureRay(nel?.PublishMagic?.Ray),
                CasterKind = CasterKind(nel?.Caster), MagicCasterKind = CasterKind(nel?.PublishMagic?.Caster),
                HasAttackFrom = attack.AttackFrom != null
            };
            if (attack.SerDmg != null)
                result.Status = attack.SerDmg.getRawObject().ToDictionary(pair => (int)pair.Key, pair => pair.Value);
            if (nel?.PublishMagic != null)
            {
                result.Magic = Capture(nel.PublishMagic, typeof(MagicItem));
                result.Magic["projectile_power"] = nel.PublishMagic.projectile_power;
            }
            if (core?.EpDmg != null)
            {
                result.Ep = Capture(core.EpDmg, typeof(EpAtk));
                result.EpKey = core.EpDmg.situation_key;
                result.EpTargets = Enumerable.Range(0, 11).Select(i => core.EpDmg.Get(i)).ToArray();
            }
            if (nel?.Beto != null)
            {
                result.Beto = Capture(nel.Beto, typeof(BetoInfo));
                result.Beto["Col"] = Pack(nel.Beto.Col);
                result.Beto["Col2"] = Pack(nel.Beto.Col2);
                result.Beto["BloodReplaceCol"] = Pack(nel.Beto.BloodReplaceCol);
            }
            return result;
        }
        static uint Pack(Color32 color) => (uint)(color.r | color.g << 8 | color.b << 16 | color.a << 24);
        static Color32 Unpack(double value)
        {
            uint color = (uint)value;
            return new Color32((byte)color, (byte)(color >> 8), (byte)(color >> 16), (byte)(color >> 24));
        }
        static bool Valid(Dictionary<string, double> values)
            => values == null || (values.Count <= 128 && values.All(pair => pair.Key.Length <= 64
                && !double.IsNaN(pair.Value) && !double.IsInfinity(pair.Value) && Math.Abs(pair.Value) <= uint.MaxValue));

        public static bool IsValid(CombatAttack attack)
            => attack != null && attack.Values != null && Valid(attack.Values) && Valid(attack.Magic)
                && Valid(attack.Ep) && Valid(attack.Beto) && (attack.EpTargets == null || attack.EpTargets.Length == 11)
                && (attack.EpKey == null || attack.EpKey.Length <= 256)
                && (attack.Status == null || (attack.Status.Count <= 128 && attack.Status.All(pair =>
                    Enum.IsDefined(typeof(SER), pair.Key) && !float.IsNaN(pair.Value)
                    && !float.IsInfinity(pair.Value) && Math.Abs(pair.Value) <= 1000000)))
                && Finite(attack.CenterX) && Finite(attack.CenterY) && Finite(attack.HitX) && Finite(attack.HitY)
                && Finite(attack.Radius) && ValidRay(attack.Ray)
                && attack.CasterKind >= 0 && attack.CasterKind <= 2 && attack.MagicCasterKind >= 0 && attack.MagicCasterKind <= 2
                && (attack.HitEffect == null || attack.HitEffect.Length <= 256) && (attack.Sound == null || attack.Sound.Length <= 256);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) < 1000000;

        public static NelAttackInfo Restore(CombatAttack data, M2Mover source)
        {
            var result = new NelAttackInfo();
            Restore(result, typeof(NelAttackInfo), data.Values);
            result.CenterXy(data.CenterX, data.CenterY, data.Radius);
            result.HitXy(data.HitX, data.HitY, true);
            result.hit_ptcst_name = data.HitEffect ?? "";
            result.snd_name = data.Sound ?? "";
            result.Caster = RestoreCaster(data.CasterKind, source);
            result.AttackFrom = data.HasAttackFrom ? source as M2Attackable : null;
            if (data.Status != null)
            {
                result.SerDmg = new FlagCounter<SER>();
                foreach (var pair in data.Status) result.SerDmg.Add((SER)pair.Key, pair.Value);
            }
            if (data.Ep != null)
            {
                result.EpDmg = new EpAtk(0, data.EpKey);
                if (data.EpTargets != null)
                    for (int i = 0; i < 11; i++) result.EpDmg.Set(i, data.EpTargets[i]);
                Restore(result.EpDmg, typeof(EpAtk), data.Ep);
            }
            if (data.Beto != null)
            {
                result.Beto = new BetoInfo();
                Restore(result.Beto, typeof(BetoInfo), data.Beto);
                if (data.Beto.TryGetValue("Col", out var col)) result.Beto.Col = Unpack(col);
                if (data.Beto.TryGetValue("Col2", out col)) result.Beto.Col2 = Unpack(col);
                if (data.Beto.TryGetValue("BloodReplaceCol", out col)) result.Beto.BloodReplaceCol = Unpack(col);
            }
            if (data.Magic != null)
            {
                // A context object, never added to the running spell list. Keep normal/chanted,
                // projectile and shotgun metadata for original dodge/parry/defense rules.
                var magic = new MagicItem(DB.MainPR.NM2D.MGC);
                Restore(magic, typeof(MagicItem), data.Magic);
                magic.Caster = RestoreCaster(data.MagicCasterKind, source);
                if (data.Magic.TryGetValue("projectile_power", out var power)) magic.projectile_power = (int)power;
                magic.changeRay(magic.MGC.makeRay(magic, Math.Max(0, data.Radius)));
                if (data.Ray != null) RestoreRay(magic.Ray, data.Ray);
                result.PublishMagic = magic;
            }
            return result;
        }
        static int CasterKind(M2MagicCaster caster) => caster == null ? 0 : caster is M2ShieldHitable ? 2 : caster is M2Mover ? 1 : -1;
        static M2MagicCaster RestoreCaster(int kind, M2Mover source)
        {
            if (kind == 0) return null;
            if (kind == 1) return source as M2MagicCaster;
            if (source is PR player && player.Skill.ShE.Shield.Hitable != null) return player.Skill.ShE.Shield.Hitable;
            throw new InvalidOperationException("Source shield context is not available");
        }
        public static CombatRay CaptureRay(M2Ray ray)
        {
            if (ray == null) return null;
            var position = ray.getMapPos();
            return new CombatRay { X = position.x, Y = position.y, DirectionX = ray.Dir.x, DirectionY = ray.Dir.y,
                Length = ray.lenmp, Radius = ray.radius_map, Power = ray.projectile_power, HitType = (int)ray.hittype,
                WeakType = (int)ray.hittype_to_week_projectile, MinimumPower = ray.cohitable_min_projectile };
        }
        public static bool ValidRay(CombatRay ray) => ray == null || (Finite(ray.X) && Finite(ray.Y)
            && Finite(ray.DirectionX) && Finite(ray.DirectionY) && Finite(ray.Length) && Finite(ray.Radius));
        public static void RestoreRay(M2Ray ray, CombatRay data)
        {
            ray.PosMap(data.X, data.Y);
            ray.Dir = new Vector2(data.DirectionX, data.DirectionY);
            ray.LenM(data.Length); ray.RadiusM(Math.Max(0, data.Radius));
            ray.projectile_power = data.Power; ray.hittype = (HITTYPE)data.HitType;
            ray.hittype_to_week_projectile = (HITTYPE)data.WeakType; ray.cohitable_min_projectile = data.MinimumPower;
        }
        public static void ReleaseContext(MagicItem magic)
        {
            if (magic?.Ray == null) return;
            var defendingRay = DB.MainPR?.Skill?.getCurSkill()?.Ray;
            if (defendingRay?.ReflectAnotherRay == magic.Ray)
            {
                // The native reflect code keeps this reference until the next skill tick.
                // Do not leave a pointer to a ray that is about to return to the pool.
                var detached = new M2Ray(null).Set(DB.MainPR.Mp, magic.Caster as M2Mover);
                RestoreRay(detached, CaptureRay(magic.Ray));
                defendingRay.ReflectAnotherRay = detached;
            }
            magic.changeRay(null);
        }
    }
}
