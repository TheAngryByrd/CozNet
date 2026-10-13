sealed class MeasurementWindow
{
    public int Milliseconds { get; }

    private MeasurementWindow(int milliseconds) => Milliseconds = milliseconds;

    public static MeasurementWindow Parse(string value) => value switch
    {
        "500" => new(500),
        "2000" => new(2000),
        _ => throw new ArgumentException("Select a measurement window of 500 or 2000 milliseconds.")
    };
}
