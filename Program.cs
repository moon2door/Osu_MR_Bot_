using Microsoft.Extensions.Configuration;
using Osu_MR_Bot.Services;
using System.Reflection.Metadata;
using System;
using System.Linq; // Array.Exists, string.Join 등을 위해 추가

namespace Osu_MR_Bot
{
    class Program
    {
        // 허용된 스타일 목록을 상수로 정의
        private static readonly string[] AllowedStyles = { "farm", "generic", "stream", "tech" };

        static async Task Main(string[] args)
        {
            // 1. 설정 파일 로드 준비
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // 2. 설정 파일에서 값 가져오기
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

            // 4. osu! 서버 연결
            await bot.ConnectAsync();

            // 5. IRC 서비스 시작 (비동기로 실행)
            OsuIrcService ircService = new OsuIrcService(botUsername, ircPassword, bot);
            // await를 제거하고 Task로 실행시켜 콘솔 입력이 가능하게 함
            Task ircTask = ircService.StartAsync();

            Console.WriteLine("\n==================================================");
            Console.WriteLine(" [System] 봇이 실행 중입니다.");
            Console.WriteLine(" - 't' 입력 후 엔터: 나 자신에게 '!m r start' 명령 실행");
            Console.WriteLine(" - '!m o [맵ID] [스타일]' 입력 후 엔터: 맵 등록 명령 실행");
            Console.WriteLine($"   (스타일: {string.Join(", ", AllowedStyles)})");
            Console.WriteLine(" - 'q' 입력 후 엔터: 프로그램 종료");
            Console.WriteLine("==================================================\n");

            // 6. 콘솔 입력 대기 루프 (메인 기능)
            while (true)
            {
                string? input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input)) continue;

                string trimmedInput = input.Trim();

                // 't' 입력 시 (!m r start)
                if (trimmedInput.ToLower() == "t")
                {
                    Console.WriteLine($"\n[Manual] 관리자 수동 명령 감지: !m r start ({botUsername})");

                    // IRC 메시지 전송 로직을 콜백으로 전달
                    await bot.ExecuteStartCommandAsync(botUsername, async (msg) =>
                    {
                        // 봇 자신에게 메시지를 보내도록 호출
                        await ircService.SendIrcMessageAsync(botUsername, msg);
                    });

                    Console.WriteLine("[Manual] 실행 완료. 대기 중...\n");
                }
                // [추가] !m o [맵번호] [스타일] 명령어 처리
                else if (trimmedInput.ToLower().StartsWith("!m o "))
                {
                    string[] parts = trimmedInput.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length == 4 && int.TryParse(parts[2], out int mapId))
                    {
                        string style = parts[3].ToLower();

                        if (Array.Exists(AllowedStyles, s => s == style))
                        {
                            Console.WriteLine($"\n[Manual] 맵 등록 명령 감지: {mapId} -> {style}");

                            // [수정] botUsername 인자 추가
                            await bot.RegisterMapStyleAsync(botUsername, mapId, style, async (msg) =>
                            {
                                // 봇 자신에게 메시지를 보내도록 호출
                                await ircService.SendIrcMessageAsync(botUsername, msg);
                            });

                            Console.WriteLine("[Manual] 실행 완료. 대기 중...\n");
                        }
                        else
                        {
                            Console.WriteLine($"[Error] 유효하지 않은 스타일입니다. 유효한 스타일: {string.Join(", ", AllowedStyles)}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("[Error] 사용법: !m o [맵번호(숫자)] [스타일]");
                    }
                }
                // 'q' 입력 시 종료
                else if (trimmedInput.ToLower() == "q")
                {
                    Console.WriteLine("[System] 프로그램을 종료합니다.");
                    break;
                }
            }
        }
    }
}