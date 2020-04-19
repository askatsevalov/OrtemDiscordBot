using Discord;
using Discord.WebSocket;
using System;
using System.Threading.Tasks;
using System.Configuration;
using System.Collections.Specialized;
using Discord.Commands;
using System.Diagnostics;
using Discord.Audio;
using Discord.Rest;

namespace OrtemDiscordBot
{
    class Program
    {
        NameValueCollection config = ConfigurationManager.AppSettings;

        private DiscordSocketClient client;
        private CommandService cmdService;
        private CommandHandler cmdHandler;
        private LogService logService;

        internal static SocketGuild guild8;

        internal static SocketVoiceChannel govor;
        internal static SocketTextChannel pisal;
        internal static SocketTextChannel test;
        internal static SocketTextChannel home;
        internal static SocketTextChannel mafia;

        internal static SocketUser Ruslan;
        internal static SocketUser Zafar;
        internal static SocketUser Andrey;
        internal static SocketUser Vadim;
        internal static SocketUser Ortem;
        internal static SocketUser Lera;
        internal static SocketUser Lera2;
        internal static SocketUser Nekit;

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

            client.Ready += () =>
            {
                guild8 = client.GetGuild(Convert.ToUInt64(config.Get("guild8_id")));

                govor = client.GetChannel(Convert.ToUInt64(config.Get("govor_id"))) as SocketVoiceChannel;
                pisal = client.GetChannel(Convert.ToUInt64(config.Get("pisal_id"))) as SocketTextChannel;
                test = client.GetChannel(Convert.ToUInt64(config.Get("test_id"))) as SocketTextChannel;
                home = client.GetChannel(Convert.ToUInt64(config.Get("home_id"))) as SocketTextChannel;
                mafia = client.GetChannel(Convert.ToUInt64(config.Get("mafia_id"))) as SocketTextChannel;

                Ruslan = client.GetUser(Convert.ToUInt64(config.Get("ruslan_id")));
                Zafar = client.GetUser(Convert.ToUInt64(config.Get("zafar_id")));
                Andrey = client.GetUser(Convert.ToUInt64(config.Get("andrey_id")));
                Vadim = client.GetUser(Convert.ToUInt64(config.Get("vadim_id")));
                Ortem = client.GetUser(Convert.ToUInt64(config.Get("ortem_id")));
                Lera = client.GetUser(Convert.ToUInt64(config.Get("lera_id")));
                Lera2 = client.GetUser(Convert.ToUInt64(config.Get("lera2_id")));
                Nekit = client.GetUser(Convert.ToUInt64(config.Get("nekit_id")));

                return Task.CompletedTask;
            };

            client.UserVoiceStateUpdated += async (SocketUser user, SocketVoiceState before, SocketVoiceState after) =>
            {
                if (user.IsBot) return;
                if (after.VoiceChannel != null && before.VoiceChannel == null)
                    await (after.VoiceChannel.Guild.Id == guild8.Id ? test : home).SendMessageAsync($"$salam {user.Discriminator}");
                else if (before.VoiceChannel != null && after.VoiceChannel == null)
                    await (before.VoiceChannel.Guild.Id == guild8.Id ? test : home).SendMessageAsync($"$leave");
            };
            client.MessageReceived += async (SocketMessage msg) =>
            {
                if (msg.Author.IsBot && msg.Content.StartsWith('$')) await msg.DeleteAsync();
            };

            client.ReactionAdded += async (Cacheable<IUserMessage, ulong> cache, ISocketMessageChannel channel, SocketReaction reaction) =>
            {
                RestUserMessage msg = (RestUserMessage)(await channel.GetMessageAsync(reaction.MessageId));
                if (msg != null && channel == mafia && msg.Author.IsBot)
                    foreach (IEmote em in msg.Reactions.Keys)
                        if (reaction.Emote.Name != em.Name) await msg.RemoveReactionAsync(em, reaction.User.Value);
            };

            await Task.Delay(-1);
        }
    }
}
