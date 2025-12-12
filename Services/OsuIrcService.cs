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

                _tcpClient = new TcpClient("irc.ppy.sh", 6667);
                var stream = _tcpClient.GetStream();

                _reader = new StreamReader(stream);
                _writer = new StreamWriter(stream) { AutoFlush = true };

                await _writer.WriteLineAsync($"PASS {_ircPassword}");
                await _writer.WriteLineAsync($"NICK {_botUsername}");

                Console.WriteLine("[IRC] 패스워드 전송 완료.");

                while (_tcpClient.Connected)
                {
                    string line = await _reader.ReadLineAsync();
                    if (line == null) break;

                    if (line.StartsWith("PING"))
                    {
                        string pong = line.Replace("PING", "PONG");
                        await _writer.WriteLineAsync(pong);
                        Console.WriteLine("[IRC] PONG");
                        continue;
                    }

                    if (line.Contains("001 "))
                    {
                        Console.WriteLine($"\n[IRC] 로그인 성공! (8글자 비번 확인됨)\n");
                    }

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

        // 메시지 전송 헬퍼 메서드
        public async Task SendIrcMessageAsync(string target, string message)
        {
            if (_writer != null && _tcpClient.Connected)
            {
                await _writer.WriteLineAsync($"PRIVMSG {target} :{message}");
                Console.WriteLine($"[IRC Send] To {target}: {message}");
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

                    // [수정] 콜백 함수 전달
                    // (msg) => SendIrcMessageAsync(sender, msg) 부분임
                    // 봇 서비스가 메시지를 보내달라고 요청하면, 이 람다 함수가 실행되어 IRC로 전송함
                    _ = _botService.ExecuteStartCommandAsync(sender, async (msg) =>
                    {
                        await SendIrcMessageAsync(sender, msg);
                    });
                }
            }
            catch { }
        }
    }
}