namespace PoultryFarm.Domain.Common;

public enum UserRole
{
    SystemAdmin = 0,
    Admin = 1,
    Worker = 2,
    SubAdmin = 3,
    MarketplaceBuyer = 4
}

public enum BatchStatus
{
    Active = 0,
    Sold = 1,
    Archived = 2
}

public enum BirdType
{
    Layer = 0,
    Broiler = 1
}

public enum VariantColor
{
    White = 0,
    Brown = 1,
    Black = 2,
    Speckled = 3,
    Mixed = 4
}

public enum EggColor
{
    White = 0,
    Brown = 1,
    Mixed = 2
}

public enum EggCollectionType
{
    Sorted = 0,
    Unsorted = 1
}

public enum EggSize
{
    Small = 0,
    Medium = 1,
    Large = 2,
    ExtraLarge = 3,
    Unsorted = 4
}

public enum FeedType
{
    LayerPremium = 0,
    LayerStandard = 1,
    Starter = 2,
    Grower = 3
}

public enum ScheduleStatus
{
    Scheduled = 0,
    Completed = 1,
    Overdue = 2
}

public enum PaymentType
{
    Cash = 0,
    Credit = 1
}

public enum PaymentStatus
{
    Paid = 0,
    OnCredit = 1
}

public enum BirdHealthStatus
{
    Dead = 0,
    Sick = 1
}

public enum FarmerRegistrationStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public enum MarketplaceListingType
{
    Eggs = 0,
    Birds = 1
}

public enum MarketplaceListingStatus
{
    Draft = 0,
    Published = 1,
    SoldOut = 2,
    Withdrawn = 3
}

public enum MarketplaceInquiryStatus
{
    New = 0,
    Contacted = 1,
    Closed = 2
}
