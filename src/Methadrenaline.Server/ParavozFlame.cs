using System.Collections.Generic;
using AdminToys;
using LabApi.Features.Wrappers;
using PlayerRoles;
using UnityEngine;
using PrimitiveToy = LabApi.Features.Wrappers.PrimitiveObjectToy;

namespace Methadrenaline.Server;

internal static class ParavozFlame
{
    internal const int Segments = 6;
    internal const float Range = 5;
    private const float Clearance = 0.3f;

    internal static void Create(Transform parent, List<PrimitiveToy> flames, List<AdminToy> toys)
    {
        for (int i = 0; i < Segments * 2; i++)
        {
            var flame = PrimitiveToy.Create(Vector3.zero, parent, networkSpawn: false);
            toys.Add(flame);
            flames.Add(flame);
            flame.Type = PrimitiveType.Sphere;
            flame.Flags = PrimitiveFlags.Visible;
            flame.Color = i % 2 == 0 ? new Color(1, 0.15f, 0.015f, 0.55f) : new Color(1, 0.72f, 0.12f, 0.8f);
        }
    }

    internal static Vector3 Mouth(Transform camera) => camera.position - camera.up * 0.32f + camera.forward * 0.2f;

    internal static float ClearRange(Vector3 mouth, Vector3 forward)
    {
        int mask = PlayerRolesUtils.AttackMask;
        if (Physics.CheckSphere(mouth, Clearance, mask, QueryTriggerInteraction.Ignore)) return 0;
        return Physics.SphereCast(mouth, Clearance, forward, out RaycastHit hit, Range, mask, QueryTriggerInteraction.Ignore)
            ? Mathf.Max(0, hit.distance - 0.05f) : Range;
    }

    internal static void Animate(Transform parent, Transform camera, IReadOnlyList<PrimitiveToy> flames, float now)
    {
        Vector3 mouth = Mouth(camera);
        Vector3 forward = camera.forward;
        float reach = ClearRange(mouth, forward);
        Quaternion rotation = Quaternion.Inverse(parent.rotation) * camera.rotation;
        for (int i = 0; i < Segments; i++)
        {
            float phase = Mathf.Repeat(now * 1.4f + (float)i / Segments, 1);
            float distance = Mathf.Lerp(0.08f, Range, phase);
            float envelope = Mathf.Sin(phase * Mathf.PI);
            float length = Mathf.Min(0.65f + envelope * 0.7f, 2 * Mathf.Min(distance, reach - distance));
            float width = Mathf.Min(Clearance * 2, 0.12f + envelope * 0.45f);
            bool visible = length > 0.01f;
            float sway = Mathf.Sin(now * 13 + i * 2.1f) * envelope * 0.025f;
            Vector3 world = mouth + forward * Mathf.Min(distance, reach) + camera.right * sway;
            for (int layer = 0; layer < 2; layer++)
            {
                var flame = flames[i * 2 + layer];
                flame.Position = parent.InverseTransformPoint(world);
                flame.Rotation = rotation;
                float size = layer == 0 ? 1 : 0.58f;
                flame.Scale = visible ? new Vector3(width * size * 0.88f, width * size * 0.88f, length * size) : Vector3.zero;
            }
        }
    }
}
