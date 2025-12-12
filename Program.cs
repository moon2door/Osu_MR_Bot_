using Microsoft.Extensions.Configuration;
using Osu_MR_Bot.Services;

namespace Osu_MR_Bot
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // 1. 설정 파일 로드
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // 2. 설정 값 가져오기
            int clientId = int.Parse(config["Osu:ClientId"]);
            string clientSecret = config["Osu:ClientSecret"];

            string firebaseUrl = config["Firebase:Url"];
            string firebaseSecret = config["Firebase:Secret"];

            string botUsername = config["Irc:Username"].Trim();
            string ircPassword = config["Irc:Password"].Trim();

            Console.WriteLine($"[Debug] 닉네임: '{botUsername}'");
            Console.WriteLine("[System] 설정 파일을 성공적으로 불러왔습니다.");

            // 3. 봇 서비스 초기화
            OsuBotService bot = new OsuBotService(clientId, clientSecret, firebaseUrl, firebaseSecret);

            // 4. osu! API 연결
            await bot.ConnectAsync();

            // 5. IRC 서비스 시작 (비동기로 실행)
            OsuIrcService ircService = new OsuIrcService(botUsername, ircPassword, bot);
            Task ircTask = ircService.StartAsync();

            Console.WriteLine("\n==================================================");
            Console.WriteLine(" [System] 봇이 실행 중입니다.");
            Console.WriteLine(" - 't' 입력 후 엔터: 나 자신에게 '!m r start' 명령 실행");
            Console.WriteLine(" - 'q' 입력 후 엔터: 프로그램 종료");
            Console.WriteLine("==================================================\n");

            // 6. 콘솔 입력 대기 루프 (메인 기능)
            while (true)
            {
                string? input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input)) continue;

                if (input.Trim().ToLower() == "t")
                {
                    Console.WriteLine($"\n[Manual] 관리자 수동 명령 감지: !m r start ({botUsername})");

                    await bot.ExecuteStartCommandAsync(botUsername, async (msg) =>
                    {
                        await ircService.SendIrcMessageAsync(botUsername, msg);
                    });

                    Console.WriteLine("[Manual] 실행 완료. 대기 중...\n");
                }
                else if (input.Trim().ToLower() == "q")
                {
                    Console.WriteLine("[System] 프로그램을 종료합니다.");
                    break;
                }
            }
        }
    }
}