using System;
using MsBox.Avalonia;
public class T
{
    public static void Main()
    {
        foreach (var m in typeof(MessageBoxManager).GetMethods())
        {
            if (m.Name.StartsWith("GetMessageBox"))
            {
                Console.Write(m.Name + "(");
                var p = m.GetParameters();
                for (int i = 0; i < p.Length; i++)
                {
                    Console.Write(p[i].ParameterType.Name + " " + p[i].Name + (i < p.Length - 1 ? ", " : ""));
                }
                Console.WriteLine(")");
            }
        }
    }
}
