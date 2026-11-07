namespace SecretAPI.Extensions.Items;

using LabApi.Features.Wrappers;

public static partial class ItemExtensions
{
    extension<T>(T item)
        where T : FirearmItem
    {
        public T WithStoredAmmo(int ammo)
        {
            item.StoredAmmo = ammo;
            return item;
        }

        public T WithMagazineInserted(bool magazineInserted)
        {
            item.MagazineInserted = magazineInserted;
            return item;
        }

        public T WithCocked(bool cocked)
        {
            item.Cocked = cocked;
            return item;
        }

        public T WithChamberedAmmo(int ammo)
        {
            item.ChamberedAmmo = ammo;
            return item;
        }
    }
}
