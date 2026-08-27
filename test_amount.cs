using System;
using System.Globalization;

class Program {
    static void Main() {
        long amountCents = 1470000;
        var amount = (amountCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        Console.WriteLine(amount);
    }
}
