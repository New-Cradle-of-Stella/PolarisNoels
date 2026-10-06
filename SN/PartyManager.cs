using UnityEngine;
using PolarisNoels.DataStruct;

namespace PolarisNoels.SN
{
    public class PartyManager
    {
        public static Party InitNewParty(int id)
        {
            float r = Random.Range(0f, 1f);
            float g = Random.Range(0f, 1f);
            float b = Random.Range(0f, 1f);
            return new()
            {
                ID = id,
                Color = new Color(r, g, b),
                Name = $"NoelParty#{id}"
            };
        }

        public class Party
        {
            public int ID;
            public ColorIns Color;
            public string Name;

            public static implicit operator Party(PartyConfig config)
            {
                return new()
                {
                    ID = config.ID,
                    Color = new(config.R, config.G, config.B, config.A),
                    Name = new(config.Name)
                };
            }
        }
    }

    public struct ColorIns(float r, float g, float b, float a)
    {
        public float r = r;
        public float g = g;
        public float b = b;
        public float a = a;

        public static implicit operator ColorIns(Color ins)
        {
            return new(ins.r, ins.g, ins.b, ins.a);
        }

        public static implicit operator Color(ColorIns ins)
        {
            return new(ins.r, ins.g, ins.b, ins.a);
        }
    }
}
