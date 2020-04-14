using Discord;
using Discord.Audio;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
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
            SocketVoiceChannel ch = Program.govor;
            IAudioClient audioClient = await ch.ConnectAsync();

            Process pr = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-hide_banner -loglevel panic -i \"audio/salam_{discr}.mp3\" -ac 2 -f s16le -ar 48000 pipe:1",
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
            SocketVoiceChannel ch = Program.govor;
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
            CurrentStage = "Начало";
            StringBuilder str_players = new StringBuilder("Начинаем игру Мафия. Список игроков:\n");
            foreach (SocketUser p in players) str_players.Append($"{p.Username}\n");
            await Program.mafia.SendMessageAsync(str_players.ToString());
            //===========
        }
    }
}
