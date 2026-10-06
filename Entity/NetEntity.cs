using System.Collections.Generic;
using UnityEngine;
using PolarisNoels.DataStruct;

namespace PolarisNoels
{
    public enum EntityRole
    {
        /// <summary>本机拥有的实体：每帧把状态发给其他玩家。</summary>
        Authority,
        /// <summary>其他玩家拥有的实体在本机的副本：只接收状态。</summary>
        Replica
    }

    /// <summary>
    /// 统一的实体同步器，挂在任意 Mover 的 GameObject 上。
    /// 具体同步什么由挂载的 <see cref="IEntityModule"/> 决定。
    /// </summary>
    public class NetEntity : MonoBehaviour
    {
        readonly List<IEntityModule> modules = [];

        bool registered;

        public int Id { get; private set; } = -1;

        public EntityRole Role { get; private set; }

        public EntityKind Kind { get; private set; }

        /// <summary>该实体的所有者（Authority 一方）的 PeerId。</summary>
        public int OwnerPeer { get; private set; }
        public EntitySpawn SpawnInfo { get; internal set; }
        string initialMapKey;
        public string MapKey => IsLocalPlayer ? DB.MainPR?.Mp?.key : initialMapKey;

        public bool IsAuthority => Role == EntityRole.Authority;

        /// <summary>本机玩家的 ID 要等握手完成后才确定，需要延后注册。</summary>
        bool IsLocalPlayer => Kind == EntityKind.Noel && Role == EntityRole.Authority;

        public NetEntity Setup(int id, int ownerPeer, EntityRole role, EntityKind kind, params IEntityModule[] mods)
        {
            Id = id;
            OwnerPeer = ownerPeer;
            Role = role;
            Kind = kind;
            initialMapKey = DB.MainPR?.Mp?.key;
            foreach (IEntityModule module in mods)
            {
                modules.Add(module);
                module.Attach(this);
            }
            if (!IsLocalPlayer)
            {
                Register();
            }
            return this;
        }

        public T GetModule<T>() where T : class, IEntityModule
        {
            foreach (IEntityModule module in modules)
            {
                if (module is T typed)
                {
                    return typed;
                }
            }
            return null;
        }

        public void BindReplicaId(int id)
        {
            if (Role != EntityRole.Replica || registered || id < 0) return;
            Id = id;
            OwnerPeer = EntityIds.Owner(id);
            Register();
        }

        void Register()
        {
            if (!registered && Id >= 0)
            {
                EntityRegistry.Register(this);
                registered = true;
            }
        }

        void Update()
        {
            if (IsLocalPlayer && !registered && PolarisNoelsTools.LocalID >= 0)
            {
                Id = EntityIds.ForPlayer(PolarisNoelsTools.LocalID);
                OwnerPeer = PolarisNoelsTools.LocalID;
                Register();
            }
            if (!registered)
            {
                return;
            }
            if (IsAuthority)
            {
                EntityNet.SendState(this);
            }
            else
            {
                foreach (IEntityModule module in modules)
                {
                    module.Tick();
                }
            }
        }

        public void WriteState(EntityState state)
        {
            foreach (IEntityModule module in modules)
            {
                module.Write(state);
            }
        }

        public void ReadState(EntityState state)
        {
            foreach (IEntityModule module in modules)
            {
                module.Read(state);
            }
        }

        public void HandleEvent(EntityEvent ev)
        {
            foreach (IEntityModule module in modules)
            {
                module.OnEvent(ev);
            }
        }

        /// <summary>副本在本机被打：把伤害转发给所有者结算。</summary>
        public void ReportLocalDamage(int hp, int mp)
        {
            if (Role != EntityRole.Replica || !registered)
            {
                return;
            }
            EntityNet.SendEvent(Id, new EntityEvent
            {
                Type = EntityEventType.Damage,
                Damage = new NotifyNoelDamage { Hp = hp, Mp = mp }
            });
        }

        void OnDestroy()
        {
            if (!registered)
            {
                return;
            }
            EntityRegistry.Unregister(this);
            registered = false;
            if (IsAuthority && Kind != EntityKind.Noel)
            {
                EntityNet.SendDespawn(Id);
            }
        }
    }
}
