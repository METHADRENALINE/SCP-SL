namespace SecretAPI.Extensions.AdminToys;

using LabApi.Features.Wrappers;
using UnityEngine;

public static partial class AdminToyExtensions
{
    extension(WaypointToy toy)
    {
        public WaypointToy WithBoundsSize(Vector3 size)
        {
            toy.BoundsSize = size;
            return toy;
        }

        public WaypointToy WithVisualiseBounds(bool visible)
        {
            toy.VisualizeBounds = visible;
            return toy;
        }

        public WaypointToy WithPriorityBias(float bias)
        {
            toy.PriorityBias = bias;
            return toy;
        }
    }
}
