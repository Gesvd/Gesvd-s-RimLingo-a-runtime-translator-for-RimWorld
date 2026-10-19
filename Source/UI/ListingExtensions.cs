using Verse;

namespace RuntimeTranslator
{
    public static class ListingExtensions
    {
        public static bool CheckboxLabeledReturn(this Listing_Standard listing, string label, bool value)
        {
            bool result = value;
            listing.CheckboxLabeled(label, ref result);
            return result;
        }
    }
}
