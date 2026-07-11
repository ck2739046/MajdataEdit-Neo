using System;
using MajSimai;
class Program
{
    static void Main()
    {
        Console.WriteLine("SimaiChart is struct: " + typeof(SimaiChart).IsValueType);
        Console.WriteLine("SimaiFile is struct: " + typeof(SimaiFile).IsValueType);
    }
}
