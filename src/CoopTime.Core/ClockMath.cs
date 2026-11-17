using System.Globalization;
using System.Numerics;

namespace CoopTime.Core;

public sealed class ClockMath
{
    public int ToMilliseconds(GameMinutes worldMinutes, int millisecondsPerTenMinutes)
    {
        GameMinutes exactMilliseconds = worldMinutes * millisecondsPerTenMinutes / 10 + millisecondRemainder;
        int whole = (int)exactMilliseconds.Floor();
        millisecondRemainder = exactMilliseconds - whole;
        return whole;
    }

    public void Reset()
    {
        millisecondRemainder = 0;
    }

    internal void RestoreRemainder(GameMinutes remainder)
    {
        millisecondRemainder = remainder;
    }

    public static GameMinutes ToMinutes(int elapsedMilliseconds, int millisecondsPerTenMinutes)
    {
        return new GameMinutes((BigInteger)elapsedMilliseconds * 10, millisecondsPerTenMinutes);
    }

    public static string Display(GameMinutes minutes)
    {
        if (minutes <= 0)
        {
            return "00:00";
        }

        BigInteger tenMinuteSteps = ((minutes + 5) / 10).Floor();
        BigInteger hours = BigInteger.DivRem(tenMinuteSteps, 6, out BigInteger remainder);
        return string.Create(CultureInfo.InvariantCulture, $"{hours:D2}:{remainder * 10:D2}");
    }

    public GameMinutes MillisecondRemainder => millisecondRemainder;

    private GameMinutes millisecondRemainder;
}
