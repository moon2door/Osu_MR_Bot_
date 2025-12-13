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
            Console.WriteLine(" - 콘솔에 명령어를 입력하면 봇 자신에게 보낸 것으로 처리됩니다.");
            Console.WriteLine(" - 예: !m r start, !m o farm 12345 67890");
            Console.WriteLine(" - [관리자] !list : 맵 기여 현황 확인");
            Console.WriteLine(" - [관리자] !m r ban [ID] : 악성 유저 밴");
            Console.WriteLine(" - 'q' 입력 후 엔터: 프로그램 종료");
            Console.WriteLine("==================================================\n");

            // 6. 콘솔 입력 대기 루프
            while (true)
            {
                string? input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input)) continue;

                string trimmedInput = input.Trim();

                // 'q' 입력 시 종료
                if (trimmedInput.ToLower() == "q")
                {
                    Console.WriteLine("[System] 프로그램을 종료합니다.");
                    break;
                }

                // [관리자 전용] !list
                if (trimmedInput.ToLower() == "!list")
                {
                    Console.WriteLine("[System] 기여 현황을 조회합니다...");
                    string stats = await bot.GetMapContributionStatsAsync();
                    Console.WriteLine(stats);
                    continue;
                }

                // [관리자 전용] 밴 명령어 처리 (!m r ban [UserId])
                if (trimmedInput.ToLower().StartsWith("!m r ban "))
                {
                    string[] parts = trimmedInput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    // parts[0]=!m, parts[1]=r, parts[2]=ban, parts[3]=UserId
                    if (parts.Length == 4 && int.TryParse(parts[3], out int banUserId))
                    {
                        await bot.BanUserAsync(banUserId);
                    }
                    else
                    {
                        Console.WriteLine("[Error] 사용법: !m r ban [유저ID(숫자)]");
                    }
                    continue; // IRC로 보내지 않음
                }

                // 그 외 명령어는 봇 로직에 전달 (IRC 처리 포함)
                Console.WriteLine($"[Manual] 콘솔 명령 실행: {trimmedInput}");
                await ircService.ProcessCommandAsync(botUsername, trimmedInput);
            }
        }
    }
}