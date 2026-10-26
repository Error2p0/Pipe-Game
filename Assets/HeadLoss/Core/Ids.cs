namespace HeadLoss.Core
{
    /// <summary>Data ids are lowercase kebab strings (<c>small-axial-fan</c>, <c>copper</c>).</summary>
    public static class Ids
    {
        /// <summary>True when <paramref name="id"/> is lowercase kebab: [a-z0-9]+(-[a-z0-9]+)*.</summary>
        public static bool IsValid(string? id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            bool lastDash = true;
            foreach (char c in id!)
            {
                bool alnum = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
                if (c == '-')
                {
                    if (lastDash) return false;
                    lastDash = true;
                }
                else if (alnum) lastDash = false;
                else return false;
            }
            return !lastDash;
        }
    }
}
