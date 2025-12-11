using Osu_MR_Bot.Services; // 서비스 네임스페이스 추가

namespace Osu_MR_Bot
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // 1. osu! API 설정
            int clientId = 46489;
            string clientSecret = "ordYuDMLKfs1JCo5Kovp6DhWmmvVTn8YZuVCumbz";

            // 2. Firebase 설정 (위 가이드를 보고 채워주세요)
            string firebaseUrl = "https://osu-mr-bot-default-rtdb.firebaseio.com/";
            string firebaseSecret = "6IVZQ47btnvzjvr7i9sd0W0Jth7yIyRKVbMKRQEy";

            // 3. IRC 설정 (채팅용)
            // osu! 닉네임 (띄어쓰기는 _ 로 대체. 예: My Name -> My_Name)
            string botUsername = "moon2door";
            // https://osu.ppy.sh/p/irc 에서 받은 비밀번호
            string ircPassword = "26156c26";

            // ==========================================

            Console.WriteLine("[System] 봇을 초기화합니다...");

            // 1. API 봇 서비스 생성
            OsuBotService botService = new OsuBotService(clientId, clientSecret, firebaseUrl, firebaseSecret);

            // 2. API 인증 (토큰 발급)
            await botService.ConnectAsync();

            // 3. IRC 채팅 서비스 생성 (API 봇 서비스를 주입)
            OsuIrcService ircService = new OsuIrcService(botUsername, ircPassword, botService);

            // 4. 채팅 리스너 시작 (여기서 무한 대기함)
            await ircService.StartAsync();
        }
    }
}