namespace XnbConverter.StardewValley.Entity;

public class SpecialOrderData
{
    public string Name;

    public string Requester;

    public string Duration;

    public string Repeatable;

    public string RequiredTags;

    public string OrderType;

    public string SpecialRule;

    public string Text;

    public string? ItemToRemoveOnEnd;

    public string? MailToRemoveOnEnd;

    public List<RandomizedElement>? RandomizedElements;

    public List<SpecialOrderObjectiveData> Objectives;

    public List<SpecialOrderRewardData> Rewards;
}