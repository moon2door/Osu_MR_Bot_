using Microsoft.Extensions.Configuration; // 이 줄 추가 필요!
using Osu_MR_Bot.Services;

namespace Osu_MR_Bot
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // 1. 설정 파일 로드 준비
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // 2. 설정 파일에서 값 가져오기
            // "Osu" 섹션 안의 "ClientId"를 가져옴
            int clientId = int.Parse(config["Osu:ClientId"]);
            string clientSecret = config["Osu:ClientSecret"];

            string firebaseUrl = config["Firebase:Url"];
            string firebaseSecret = config["Firebase:Secret"];

            string botUsername = config["Irc:Username"].Trim();
            string ircPassword = config["Irc:Password"].Trim();

            Console.WriteLine($"[Debug] 닉네임: '{botUsername}' (길이: {botUsername.Length})");
            Console.WriteLine($"[Debug] 비밀번호: 앞자리 '{ircPassword.Substring(0, 3)}...' (총 길이: {ircPassword.Length})");

            Console.WriteLine("[System] 설정 파일을 성공적으로 불러왔습니다.");

            // 3. 봇 서비스 초기화 (이전과 동일)
            OsuBotService bot = new OsuBotService(clientId, clientSecret, firebaseUrl, firebaseSecret);

            // 4. osu! 서버 연결
            await bot.ConnectAsync();

            // 5. IRC 서비스 시작
            OsuIrcService ircService = new OsuIrcService(botUsername, ircPassword, bot);
            await ircService.StartAsync();

            Console.WriteLine("[System] 프로그램이 종료되었습니다. 아무 키나 누르면 닫힙니다.");
            Console.ReadLine();
        }
    }
}