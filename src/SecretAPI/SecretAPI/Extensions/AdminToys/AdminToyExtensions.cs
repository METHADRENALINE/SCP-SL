namespace SecretAPI.Extensions.AdminToys;

using LabApi.Features.Wrappers;
using UnityEngine;
using CapybaraToy = LabApi.Features.Wrappers.CapybaraToy;

public static partial class AdminToyExtensions
{
    extension<T>(T toy)
        where T : AdminToy
    {
        public T WithPosition(Vector3 pos)
        {
            toy.Position = pos;
            return toy;
        }

        public T WithRotation(Quaternion rot)
        {
            toy.Rotation = rot;
            return toy;
        }

        public T WithScale(Vector3 scale)
        {
            toy.Scale = scale;
            return toy;
        }
    }

    public static CapybaraToy WithCollidersEnabled(this CapybaraToy toy, bool enabled)
    {
        toy.CollidersEnabled = enabled;
        return toy;
    }
}
