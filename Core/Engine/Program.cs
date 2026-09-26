using Engine;
using Engine.Compilation;
using System.IO;
using System.Reflection;

public class StartupDemo
{
    public static int Main(string[] args)
    {
        if (args != null && args.Length > 0 && args[0] == "-compile")
        {
            EntityCompiler.CompileAllEntities(Assembly.GetExecutingAssembly(), $"{Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}", false);
            EntityCompiler.CompileAllEntities(Assembly.GetAssembly(typeof(MainEngine)), $"{Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}", true);
        }
        else
        {
            EntityCompiler.ReadAllEntities($"{Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}");
        }

        //Replace "MainEngine" with your engine.
        using var game = new Engine.MainEngine();
        game.Run();

        return 0;
    }
}