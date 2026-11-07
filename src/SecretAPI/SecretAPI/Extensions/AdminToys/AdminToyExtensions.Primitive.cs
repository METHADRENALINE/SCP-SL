namespace SecretAPI.Extensions.AdminToys;

using global::AdminToys;
using UnityEngine;
using PrimitiveObjectToy = LabApi.Features.Wrappers.PrimitiveObjectToy;

public static partial class AdminToyExtensions
{
    extension(PrimitiveObjectToy toy)
    {
        public bool Collidable
        {
            get => (toy.Flags & PrimitiveFlags.Collidable) == PrimitiveFlags.Collidable;
            set => toy.Flags = value ? toy.Flags | PrimitiveFlags.Collidable : toy.Flags & ~PrimitiveFlags.Collidable;
        }

        public bool Visible
        {
            get => (toy.Flags & PrimitiveFlags.Visible) == PrimitiveFlags.Visible;
            set => toy.Flags = value ? toy.Flags | PrimitiveFlags.Visible : toy.Flags & ~PrimitiveFlags.Visible;
        }

        public PrimitiveObjectToy WithCollidable(bool collidable)
        {
            toy.Collidable = collidable;
            return toy;
        }

        public PrimitiveObjectToy WithVisible(bool visible)
        {
            toy.Visible = visible;
            return toy;
        }

        public PrimitiveObjectToy WithType(PrimitiveType type)
        {
            toy.Type = type;
            return toy;
        }

        public PrimitiveObjectToy WithColor(Color color)
        {
            toy.Color = color;
            return toy;
        }

        public PrimitiveObjectToy WithFlags(PrimitiveFlags flags)
        {
            toy.Flags = flags;
            return toy;
        }
    }
}
