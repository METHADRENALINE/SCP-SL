namespace SecretAPI.Extensions.Items;

using InventorySystem.Items.Firearms.Modules.Scp127;
using LabApi.Features.Wrappers;

public static partial class ItemExtensions
{
    extension(Scp127Firearm item)
    {
        public Scp127Firearm WithTier(Scp127Tier tier)
        {
            item.Tier = tier;
            return item;
        }

        public Scp127Firearm WithExperience(float experience)
        {
            item.Experience = experience;
            return item;
        }
    }
}
