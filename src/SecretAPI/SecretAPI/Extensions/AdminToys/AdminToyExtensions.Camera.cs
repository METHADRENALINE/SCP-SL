namespace SecretAPI.Extensions.AdminToys;

using LabApi.Features.Wrappers;
using UnityEngine;

public static partial class AdminToyExtensions
{
    extension(CameraToy toy)
    {
        public CameraToy WithLabel(string label)
        {
            toy.Label = label;
            return toy;
        }

        public CameraToy WithRoom(Room room)
        {
            toy.Room = room;
            return toy;
        }

        public CameraToy WithVerticalConstraints(Vector2 constraint)
        {
            toy.VerticalConstraints = constraint;
            return toy;
        }

        public CameraToy WithHorizontalConstraints(Vector2 constraint)
        {
            toy.HorizontalConstraint = constraint;
            return toy;
        }

        public CameraToy WithZoomConstraints(Vector2 constraint)
        {
            toy.ZoomConstraints = constraint;
            return toy;
        }
    }
}
