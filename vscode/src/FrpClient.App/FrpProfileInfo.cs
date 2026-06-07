namespace FrpClient.App;

public class FrpProfileInfo
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public override string ToString()
    {
        return Name;
    }
}
