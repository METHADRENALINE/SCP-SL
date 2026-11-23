namespace Methadrenaline.Server.Core;

internal static class FortuneCoinPolicy
{
    internal const float VoiceRange = 20f;
    internal static bool InEarshot(float distanceSquared) => distanceSquared <= VoiceRange * VoiceRange;

    internal static float LootCost(float distance, int danger, bool previouslyRecommended)
        => distance + danger - (previouslyRecommended && danger == 0 ? 50f : 0f);
}
