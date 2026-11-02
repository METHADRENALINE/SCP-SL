using System;

namespace Methadrenaline.Server.Core;

public static class BasicAsnPolicy
{
    public static bool Allows(string userId, int asn, string reason, string[] accounts)
    {
        if (string.IsNullOrEmpty(userId) || asn <= 0 || reason == null || accounts == null) return false;
        int salt = userId.IndexOf('$');
        if (salt >= 0) userId = userId.Substring(0, salt);
        if (!userId.EndsWith("@discord", StringComparison.Ordinal)) return false;
        if (Array.IndexOf(accounts, userId) < 0) return false;
        return reason.StartsWith("The ASN of the Internet Service Provider you are connecting from is globally blocked (basic level ban) - ASN" + asn + " - ", StringComparison.Ordinal);
    }
}
