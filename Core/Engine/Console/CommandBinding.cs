using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Console;

public class CommandBinding
{
    public string commandName;
    public Action<string[]> command;

    public CommandBinding(string commandName, Action<string[]> command)
    {
        this.commandName = commandName;
        this.command = command;
        Create();
    }
    public void Create()
    {
        MainEngine.commands.Enqueue(this);
        MainEngine.commandsDirty = true;
    }
}