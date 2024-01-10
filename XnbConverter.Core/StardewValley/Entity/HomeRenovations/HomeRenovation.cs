namespace XnbConverter.StardewValley.Entity.HomeRenovations;

public class HomeRenovation
{
    public string? TextStrings;

    public string? AnimationType;

    public bool CheckForObstructions;

    public List<RenovationValue>? Requirements;

    public List<RenovationValue>? RenovateActions;
    
    public List<RectGroup>? RectGroups;
    
    public string? SpecialRect;
}