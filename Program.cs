using Discord;
using Discord.WebSocket;
using System;
using System.Threading.Tasks;
using System.Configuration;
using System.Collections.Specialized;
using Discord.Commands;

namespace OrtemDiscordBot
{
    class Program
    {
        NameValueCollection config = ConfigurationManager.AppSettings;
        private DiscordSocketClient client;
        private CommandService cmdService;
        private CommandHandler cmdHandler;
        private LogService logService;

        public static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult();

        public async Task MainAsync()
        {
            //Create and start bot
            client = new DiscordSocketClient();
            cmdService = new CommandService();
            logService = new LogService(client, cmdService);
            cmdHandler = new CommandHandler(client, cmdService);
            await cmdHandler.InstallCommandsAsync();
            
            await client.LoginAsync(TokenType.Bot, config.Get("token"));
            await client.StartAsync();
            //====================

            //Add event handlers
            client.Log += (LogMessage msg) =>
            {
                Console.WriteLine(msg);
                return Task.CompletedTask;
            };
            //==================

            await Task.Delay(-1);
        }
    }
}
