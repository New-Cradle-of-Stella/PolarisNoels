using nel;
using PolarisNoels.SN;
using System;
using System.Reflection;

namespace PolarisNoels
{
    /// <summary>
    /// Damage is resolved by the target's owner, using the original attack and damage rules.
    /// Replica attacks are presentation only; accepted requests run the original target rules.
    /// </summary>
    public static class DamageAuthority
    {
        public static bool CanDamage(NelEnemy enemy)
        {
            if (!DB.IsMultiplayer || enemy == null) return true;
            if (PolarisNoelsTools.peer != null && PolarisNoelsTools.LocalID >= 0) return CombatSync.Owns(enemy);
            // Boss parts may not have an entity of their own. Inherit their parent's authority.
            for (NelEnemy current = enemy; current != null;)
            {
                if (current.TryGetComponent<NetEntity>(out var entity)) return entity.IsAuthority;
                if (current is not NelEnemyNested nested || nested.Parent == current) break;
                current = nested.Parent;
            }
            // Map enemies/NPCs outside the entity protocol keep their original behavior.
            return true;
        }

        public static bool CanDamage(PR player) => player is not ShadowNoel;

        // Skipping a damage method must still initialize outputs such as stop_carrier,
        // force or gauge_break. Preserve ref inputs; only out parameters are cleared.
        public static void ClearOutputs(MethodBase method, object[] args)
        {
            var parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (!parameters[i].IsOut) continue;
                var type = parameters[i].ParameterType.GetElementType();
                args[i] = type.IsValueType ? Activator.CreateInstance(type) : null;
            }
        }
    }
}
