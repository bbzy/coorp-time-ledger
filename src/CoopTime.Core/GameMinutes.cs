using System.Globalization;
using System.Numerics;

namespace CoopTime.Core;

// Rational game minutes preserve integer engine milliseconds across cadence changes.
public readonly struct GameMinutes : IEquatable<GameMinutes>, IComparable<GameMinutes>
{
    public GameMinutes(BigInteger numerator, BigInteger denominator)
    {
        if (denominator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator));
        }

        BigInteger gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
        this.numerator = numerator / gcd;
        this.denominator = denominator / gcd;
    }

    public int CompareTo(GameMinutes other)
    {
        return (numerator * other.Denominator).CompareTo(other.numerator * Denominator);
    }

    public bool Equals(GameMinutes other)
    {
        return CompareTo(other) == 0;
    }

    public override bool Equals(object? obj)
    {
        return obj is GameMinutes value && Equals(value);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(numerator, Denominator);
    }

    public BigInteger Floor()
    {
        BigInteger quotient = BigInteger.DivRem(numerator, Denominator, out BigInteger remainder);
        return remainder.Sign < 0 ? quotient - 1 : quotient;
    }

    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{numerator}/{Denominator}");
    }

    public static GameMinutes Min(GameMinutes a, GameMinutes b) => a <= b ? a : b;
    public static bool TryParse(string? text, out GameMinutes value)
    {
        value = default;
        string[]? parts = text?.Split('/');
        if (parts?.Length != 2
            || !BigInteger.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger numerator)
            || !BigInteger.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger denominator)
            || denominator <= 0)
        {
            return false;
        }

        value = new GameMinutes(numerator, denominator);
        return true;
    }

    public static implicit operator GameMinutes(int value) => new(value, 1);
    public static implicit operator GameMinutes(decimal value)
    {
        int[] bits = decimal.GetBits(value);
        BigInteger integer = (BigInteger)(uint)bits[0] + ((BigInteger)(uint)bits[1] << 32)
            + ((BigInteger)(uint)bits[2] << 64);
        if (value < 0)
        {
            integer = -integer;
        }

        return new GameMinutes(integer, BigInteger.Pow(10, (bits[3] >> 16) & 0xff));
    }

    public static GameMinutes operator +(GameMinutes a, GameMinutes b) =>
        new(a.numerator * b.Denominator + b.numerator * a.Denominator, a.Denominator * b.Denominator);
    public static GameMinutes operator -(GameMinutes a, GameMinutes b) =>
        new(a.numerator * b.Denominator - b.numerator * a.Denominator, a.Denominator * b.Denominator);
    public static GameMinutes operator *(GameMinutes a, int b) => new(a.numerator * b, a.Denominator);
    public static GameMinutes operator /(GameMinutes a, int b) => new(a.numerator, a.Denominator * b);
    public static bool operator ==(GameMinutes a, GameMinutes b) => a.Equals(b);
    public static bool operator !=(GameMinutes a, GameMinutes b) => !a.Equals(b);
    public static bool operator <(GameMinutes a, GameMinutes b) => a.CompareTo(b) < 0;
    public static bool operator >(GameMinutes a, GameMinutes b) => a.CompareTo(b) > 0;
    public static bool operator <=(GameMinutes a, GameMinutes b) => a.CompareTo(b) <= 0;
    public static bool operator >=(GameMinutes a, GameMinutes b) => a.CompareTo(b) >= 0;

    private BigInteger Denominator => denominator.IsZero ? BigInteger.One : denominator;

    private readonly BigInteger numerator;
    private readonly BigInteger denominator;
}
