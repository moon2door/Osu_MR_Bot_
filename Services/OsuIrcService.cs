using System.Net.Sockets;
using System.Text;

namespace Osu_MR_Bot.Services
{
    public class OsuIrcService
    {
        private readonly string _botUsername;
        private readonly string _ircPassword;
        private readonly OsuBotService _botService;

        private TcpClient _tcpClient;
        private StreamReader _reader;
        private StreamWriter _writer;

        public OsuIrcService(string botUsername, string ircPassword, OsuBotService botService)
        {
            _botUsername = botUsername;
            _ircPassword = ircPassword;
            _botService = botService;
        }

        public async Task StartAsync()
        {
            try
            {
                Console.WriteLine($"[IRC] {_botUsername} 계정으로 접속 시도 중... (Port 6667)");

                // 1. 기존 봇과 동일한 접속 설정
                _tcpClient = new TcpClient("irc.ppy.sh", 6667);
                var stream = _tcpClient.GetStream();

                // [중요 수정] Encoding.UTF8을 제거하여 BOM(특수문자) 전송 방지
                // 기존 봇: writer = new StreamWriter(stream) { AutoFlush = true };
                _reader = new StreamReader(stream); // 인코딩 제거
                _writer = new StreamWriter(stream) { AutoFlush = true }; // 인코딩 제거

                // 2. 로그인 전송 (비동기 방식은 유지하되 내용은 동일하게)
                await _writer.WriteLineAsync($"PASS {_ircPassword}");
                await _writer.WriteLineAsync($"NICK {_botUsername}");

                Console.WriteLine("[IRC] 패스워드 전송 완료.");

                // 3. 수신 루프
                while (_tcpClient.Connected)
                {
                    string line = await _reader.ReadLineAsync();
                    if (line == null) break;

                    // PING 처리
                    if (line.StartsWith("PING"))
                    {
                        string pong = line.Replace("PING", "PONG");
                        await _writer.WriteLineAsync(pong);
                        Console.WriteLine("[IRC] PONG");
                        continue;
                    }

                    // 로그인 성공 로그
                    if (line.Contains("001 "))
                    {
                        Console.WriteLine($"\n[IRC] 로그인 성공! (8글자 비번 확인됨)\n");
                    }

                    // 에러 로그
                    if (line.Contains("464"))
                    {
                        Console.WriteLine($"[Error] 비번틀림");
                        Console.WriteLine($"[Debug] 보낸 비번: {_ircPassword}");
                    }

                    // 명령어 감지
                    if (line.Contains(" PRIVMSG "))
                    {
                        HandleMessage(line);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IRC] 오류: {ex.Message}");
            }
        }

        private void HandleMessage(string rawLine)
        {
            try
            {
                int exclaimIndex = rawLine.IndexOf('!');
                if (exclaimIndex < 1) return;

                string sender = rawLine.Substring(1, exclaimIndex - 1);
                int msgIndex = rawLine.IndexOf(" :", exclaimIndex);
                if (msgIndex < 0) return;

                string message = rawLine.Substring(msgIndex + 2).Trim();

                if (message == "!m r start")
                {
                    Console.WriteLine($"[Chat] {sender} 명령어 감지!");
                    _ = _botService.ExecuteStartCommandAsync(sender);
                }
            }
            catch { }
        }
    }
}