namespace Delegat.Enums;

public enum AttributeType
{
    Text = 0,
    Number = 1,
    Boolean = 2,
    Select = 3,
    MultiSelect = 4,
    Barcode = 5,
    /// <summary>One or more files attached to the product; defaults to one unless multiple files are enabled.</summary>
    File = 6
}
