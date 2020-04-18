using Discord;
using Discord.Audio;
using Discord.Commands;
using Discord.Rest;
using Discord.WebSocket;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace OrtemDiscordBot
{
    class CommandHandler
    {
        private readonly DiscordSocketClient client;
        private readonly CommandService commands;

        public CommandHandler(DiscordSocketClient client, CommandService commands)
        {
            this.commands = commands;
            this.client = client;
        }

        public async Task InstallCommandsAsync()
        {
            client.MessageReceived += HandleCommandAsync;
            await commands.AddModulesAsync(assembly: Assembly.GetEntryAssembly(), services: null);
        }

        private async Task HandleCommandAsync(SocketMessage messageParam)
        {
            var message = messageParam as SocketUserMessage;
            if (message == null) return;

            int argPos = 0;

            if (!(message.HasCharPrefix('$', ref argPos) || message.HasMentionPrefix(client.CurrentUser, ref argPos)))
                return;

            var context = new SocketCommandContext(client, message);

            await commands.ExecuteAsync(context, argPos, null);
        }
    }
    
	public class InfoModule : ModuleBase<SocketCommandContext>
	{
		[Command("say")]
		[Summary("Echoes a message.")]
		public Task SayAsync([Remainder] [Summary("The text to echo")] string echo) => ReplyAsync(echo);

        [Command("userinfo")]
        [Summary("Returns info about the current user, or the user parameter, if one passed.")]
        [Alias("user", "whois")]
        public async Task UserInfoAsync([Summary("The (optional) user to get info from")] SocketUser user = null)
        {
            var userInfo = user ?? Context.Client.CurrentUser;
            await ReplyAsync($"{userInfo.Username}#{userInfo.Discriminator}\nСтатус: {userInfo.Status}\n{(userInfo.Activity != null ? $"Играет в {userInfo.Activity}" : "Не занят")}");
        }
    }

    public class VoiceModule : ModuleBase<SocketCommandContext>
    {
        [Command("salam", RunMode = RunMode.Async)]
        [Summary("Salams user when he connects to voice channel.")]
        public async Task SalamAsync([Summary("Discriminator of the user")]string discr)
        {
            SocketVoiceChannel ch = Context.Guild.VoiceChannels.FirstOrDefault();
            IAudioClient audioClient = await ch.ConnectAsync();
            discr = Context.Guild.Id == Program.guild8.Id ? discr : "";
            Process pr = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-hide_banner -loglevel panic -i \"audio/salam{discr}.mp3\" -ac 2 -f s16le -ar 48000 pipe:1",
                UseShellExecute = false,
                RedirectStandardOutput = true
            });

            using (var ffmpeg = pr)
            using (var output = ffmpeg.StandardOutput.BaseStream)
            using (var discord = audioClient.CreatePCMStream(AudioApplication.Mixed))
            {
                try { await output.CopyToAsync(discord); }
                finally { await discord.FlushAsync(); }
            }
            await ch.DisconnectAsync();
        }

        [Command("leave", RunMode = RunMode.Async)]
        [Summary("Says goodbye to user when he disconnects from voice channel.")]
        public async Task LeaveAsync()
        {
            SocketVoiceChannel ch = Context.Guild.VoiceChannels.FirstOrDefault();
            IAudioClient audioClient = await ch.ConnectAsync();

            Process pr = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-hide_banner -loglevel panic -i \"audio/leave.mp3\" -ac 2 -f s16le -ar 48000 pipe:1",
                UseShellExecute = false,
                RedirectStandardOutput = true
            });

            using (var ffmpeg = pr)
            using (var output = ffmpeg.StandardOutput.BaseStream)
            using (var discord = audioClient.CreatePCMStream(AudioApplication.Mixed))
            {
                try { await output.CopyToAsync(discord); }
                finally { await discord.FlushAsync(); }
            }
            await ch.DisconnectAsync();
        }
    }

    [Group("mafia")]
    public class MafiaModule : ModuleBase<SocketCommandContext>
    {
        Random rnd = new Random();

        List<SocketUser> players; //List of active (and alive) players
        string CurrentStage; //Current stage of the game

        //Roles
        List<IUser> MafiaPlayers = new List<IUser>();
        List<IUser> CitizenPlayers = new List<IUser>();
        IUser DoctorPlayer;
        IUser PolicemanPlayer;
        IUser WhorePlayer;
        //=====

        //Player States
        IUser Dead;
        IUser Healed;
        IUser Fucked;
        //=============

        //Instructions
        string MafiaInstruction = @"Ты Мафия!\n
                                    Инструкция:
                                        1. Все команды писать в этот чат.
                                        2. Когда будет объявлен раунд 'Убийство', введи команду $mafia kill *Дискриминатор игрока, которого нужно убить* (например, $mafia kill 9929). (Не забудь посоветоваться с напарником, если он есть).
                                        3. Остальные личные подсказки и инcтрукции будут показаны в этом чате.";
        string CitizenInstruction = @"Ты Мирный житель!";
        string PolicemanInstruction = @"Ты Полицейский!\n
                                    Инструкция:
                                        1. Все команды писать в этот чат.
                                        2. Когда будет объявлен раунд 'Расследование', введи команду $mafia reveal *Дискриминатор игрока, которого нужно раскрыть* (например, $mafia reveal 9929).
                                        3. Остальные личные подсказки и инcтрукции будут показаны в этом чате.";
        string DoctorInstruction = @"Ты Доктор!\n
                                    Инструкция:
                                        1. Все команды писать в этот чат.
                                        2. Когда будет объявлен раунд 'Лечение', введи команду $mafia heel *Дискриминатор игрока, которого нужно вылечить* (например, $mafia heel 9929).
                                        3. Остальные личные подсказки и инcтрукции будут показаны в этом чате.";
        string WhoreInstruction = @"Ты Куртизанка!\n
                                    Инструкция:
                                        1. Все команды писать в этот чат.
                                        2. Когда будет объявлен раунд 'Изнасилование', введи команду $mafia fuck *Дискриминатор игрока, которого нужно изнасиловать* (например, $mafia fuck 9929).
                                        3. Остальные личные подсказки и инcтрукции будут показаны в этом чате.";
        //============

        [Command("play")]
        [Summary("Begins new Mafia game.")]
        public async Task MafiaPlayAsync() {
            if (Context.Channel != Program.mafia) return;
            //Get active players
            players = new List<SocketUser>(Program.govor.Users);
            players.RemoveAll(x => x.IsBot);
            //==================

            //Give every player a role
            List<int> listNumbers = new List<int>();
            listNumbers.AddRange(Enumerable.Range(0, players.Count).OrderBy(i => rnd.Next()));
            if (players.Count < 4) await Program.test.SendMessageAsync("Количество игроков должно быть 4 и больше.");
            else if (players.Count == 4)
            {
                MafiaPlayers.Add(players[listNumbers[0]]);
                for (int i = 1; i < players.Count; i++) CitizenPlayers.Add(players[listNumbers[i]]);
            }
            else if (players.Count == 5)
            {
                MafiaPlayers.Add(players[listNumbers[0]]);
                PolicemanPlayer = players[listNumbers[1]];
                for (int i = 2; i < players.Count; i++) CitizenPlayers.Add(players[listNumbers[i]]);
            }
            else if (players.Count == 6)
            {
                MafiaPlayers.Add(players[listNumbers[0]]);
                MafiaPlayers.Add(players[listNumbers[1]]);
                PolicemanPlayer = players[listNumbers[2]];
                for (int i = 3; i < players.Count; i++) CitizenPlayers.Add(players[listNumbers[i]]);
            }
            else if (players.Count == 7)
            {
                MafiaPlayers.Add(players[listNumbers[0]]);
                MafiaPlayers.Add(players[listNumbers[1]]);
                PolicemanPlayer = players[listNumbers[2]];
                DoctorPlayer = players[listNumbers[3]];
                for (int i = 4; i < players.Count; i++) CitizenPlayers.Add(players[listNumbers[i]]);
            }
            else if (players.Count == 8)
            {
                MafiaPlayers.Add(players[listNumbers[0]]);
                MafiaPlayers.Add(players[listNumbers[1]]);
                PolicemanPlayer = players[listNumbers[2]];
                DoctorPlayer = players[listNumbers[3]];
                WhorePlayer = players[listNumbers[4]];
                for (int i = 5; i < players.Count; i++) CitizenPlayers.Add(players[listNumbers[i]]);
            }
            //========================

            //Send every player his role's instruction
            foreach (IUser m in MafiaPlayers) await m.SendMessageAsync(MafiaInstruction);
            if (MafiaPlayers.Count > 1)
            {
                await MafiaPlayers[0].SendMessageAsync($"Твой напарник - {MafiaPlayers[1].Username}#{MafiaPlayers[1].Discriminator}");
                await MafiaPlayers[1].SendMessageAsync($"Твой напарник - {MafiaPlayers[0].Username}#{MafiaPlayers[0].Discriminator}");
            }
            foreach (IUser c in CitizenPlayers) await c.SendMessageAsync(CitizenInstruction);
            if (PolicemanPlayer != null) await PolicemanPlayer?.SendMessageAsync(PolicemanInstruction);
            if (DoctorPlayer != null) await DoctorPlayer?.SendMessageAsync(DoctorInstruction);
            if (WhorePlayer != null) await WhorePlayer?.SendMessageAsync(WhoreInstruction);
            //========================================

            //Let's begin
            CurrentStage = "Killing";
            StringBuilder str_players = new StringBuilder("Начинаем игру Мафия. Список игроков:\n");
            foreach (SocketUser p in players) str_players.Append($"{p.Username}\n");
            await Program.mafia.SendMessageAsync(str_players.ToString());
            await Program.mafia.SendMessageAsync("Ожидаем ход мафии...");
            //===========
        }

        [Command("kill")]
        [Summary("Kills a player with given discriminator.")]
        public async Task MafiaKillAsync([Summary("Discriminator of the user")]string discr)
        {
            if (Program.guild8.Channels.Where(x => x.Id.Equals(Context.Channel.Id)).Count() != 0 || MafiaPlayers.Where(x => x.Id.Equals(Context.User.Id)).Count() == 0 || CurrentStage != "Killing") return;
            Dead = players.Where(x => x.Discriminator.Equals(discr)).FirstOrDefault();
            if (Dead == null ||(MafiaPlayers.Count == 1 && MafiaPlayers.FirstOrDefault().Discriminator.Equals(Dead.Discriminator)))
            {
                await Context.Channel.SendMessageAsync("Неверный дискриминатор!");
                return;
            }
            CurrentStage = "Healing";
            await Program.mafia.SendMessageAsync("Мафия сделала свой выбор.");
            await Program.mafia.SendMessageAsync("Ожидаем ход доктора...");
        }

        [Command("heal")]
        [Summary("Heals a player with given discriminator.")]
        public async Task MafiaHealAsync([Summary("Discriminator of the user")]string discr)
        {
            if (Program.guild8.Channels.Where(x => x.Id.Equals(Context.Channel.Id)).Count() != 0 || MafiaPlayers.Where(x => x.Id.Equals(Context.User.Id)).Count() == 0 || CurrentStage != "Healing") return;
            Healed = players.Where(x => x.Discriminator.Equals(discr)).FirstOrDefault();
            if (Healed == null)
            {
                await Context.Channel.SendMessageAsync("Неверный дискриминатор!");
                return;
            }
            if (Healed == Dead) Dead = null;
            CurrentStage = "Fucking";
            await Program.mafia.SendMessageAsync("Доктор сделал свой выбор.");
            await Program.mafia.SendMessageAsync("Ожидаем ход куртизанки...");
        }

        [Command("fuck")]
        [Summary("Fucks a player with given discriminator.")]
        public async Task MafiaFuckAsync([Summary("Discriminator of the user")]string discr)
        {
            //if (Program.guild8.Channels.Where(x => x.Id.Equals(Context.Channel.Id)).Count() != 0 || MafiaPlayers.Where(x => x.Id.Equals(Context.User.Id)).Count() == 0 || CurrentStage != "Fucking") return;
            Fucked = players.Where(x => x.Discriminator.Equals(discr)).FirstOrDefault();
            if (Fucked == null)
            {
                await Context.Channel.SendMessageAsync("Неверный дискриминатор!");
                return;
            }
            CurrentStage = "Voting";
            await Program.mafia.SendMessageAsync("Куртизанка сделала свой выбор.");
            var msg = await Program.mafia.SendMessageAsync("Объявляется голосование! Кого вы считаете мафией? Просьба выбирать только один вариант (временно)!");
            foreach (IUser u in players)
            {
                GuildEmote em;
                if (Program.guild8.Emotes.First(x => x.Name.Equals($"vote{u.Discriminator}")) == null) em = await Program.guild8.CreateEmoteAsync($"vote{u.Discriminator}", new Image(u.GetAvatarUrl()));
                else em = Program.guild8.Emotes.First(x => x.Name.Equals($"vote{u.Discriminator}"));
                await msg.AddReactionAsync(em);
            }
        }

        [Command("vote")]
        [Summary("Fucks a player with given discriminator.")]
        public async Task MafiaVoteAsync()
        {
            if (Program.guild8.Channels.Where(x => x.Id.Equals(Context.Channel.Id)).Count() != 0 || MafiaPlayers.Where(x => x.Id.Equals(Context.User.Id)).Count() == 0 || CurrentStage != "Voting") return;
            
        }
    }

        [Group("easter")]
    public class EasterModule : ModuleBase<SocketCommandContext>
    {
        private class UserScore
        {
            public UserScore(IUser u, int s) { user = u; score = s; }
            public IUser user { get; set; }
            public int score { get; set; }
        }

        Configuration config = ConfigurationManager.OpenExeConfiguration(Assembly.GetExecutingAssembly().Location);   

        [Command("battle")]
        [Summary("Performs an egg battle between two players.")]
        public async Task EasterBattleAsync([Summary("User to battle with.")] IUser user)
        {
            if (Context.User == user) { await Context.Channel.SendMessageAsync("Ты не можешь биться сам с собой."); return; }
            IUser winner = (new Random()).Next(0, 2) == 0 ? Context.User : user;
            IUser loser = winner.Discriminator == user.Discriminator ? Context.User : user;
            int wscore;
            if (config.AppSettings.Settings[$"score{winner.Discriminator}"] == null) config.AppSettings.Settings.Add($"score{winner.Discriminator}", "0");
             config.AppSettings.Settings[$"score{winner.Discriminator}"].Value = (wscore = Convert.ToInt32(config.AppSettings.Settings[$"score{winner.Discriminator}"].Value) + 1).ToString();
            config.Save();
            ConfigurationManager.RefreshSection("appSettings");
            string lscore = config.AppSettings.Settings[$"score{loser.Discriminator}"]?.Value ?? "0";
            await Context.Channel.SendMessageAsync($"Яйцо {winner.Mention} оказалось крепче, чем у {loser.Mention}!\nТекущий счет: {winner.Username} - {wscore}, {loser.Username} - {lscore}");
        }

        [Command("score")]
        [Summary("Demonstrates easter score of each member of guild.")]
        public async Task EasterScoreAsync()
        {
            List<UserScore> lst = new List<UserScore>();
            StringBuilder ScoreString = new StringBuilder("Общий счет пасхальной битвы:\n");
            foreach (IUser u in Context.Guild.Users)
                if (!u.IsBot)
                {
                    if (config.AppSettings.Settings[$"score{u.Discriminator}"] != null)
                        lst.Add(new UserScore(u, Convert.ToInt32(config.AppSettings.Settings[$"score{u.Discriminator}"].Value)));
                    else
                        lst.Add(new UserScore(u, 0));
                }
            lst = lst.OrderByDescending(x => x.score).ToList();
            foreach (UserScore us in lst)
                    ScoreString.Append($"{us.user.Username} - {us.score}\n");
            await Context.Channel.SendMessageAsync(ScoreString.ToString());
        }
    }
}
