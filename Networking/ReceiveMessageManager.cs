using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PolarisNoels.DataStruct;

namespace PolarisNoels.Networking
{
    public static class ReceiveMessageManager
    {
        static List<PeerReceiveMessageBase> list = [];

        public static void Init()
        {
            list.Clear();
            Type baseType = typeof(PeerReceiveMessageBase);
            var subTypes = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => t.BaseType == baseType);
            foreach (var type in subTypes)
            {
                RegisterReceiveMessage((PeerReceiveMessageBase)Activator.CreateInstance(type));
            }
        }

        public static void RegisterReceiveMessage(PeerReceiveMessageBase receive)
        {
            list.Add(receive);
        }

        public static IReadOnlyList<PeerReceiveMessageBase> GetAllReceives()
        {
            return list;
        }
    }
}
