using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

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

        private async Task HandleMessage(string rawLine)
        {
            try
            {
                int exclaimIndex = rawLine.IndexOf('!');
                if (exclaimIndex < 1) return;

                string sender = rawLine.Substring(1, exclaimIndex - 1);
                int msgIndex = rawLine.IndexOf(" :", exclaimIndex);
                if (msgIndex < 0) return;

                string message = rawLine.Substring(msgIndex + 2).Trim();

                // 1. !m r start 명령어 처리 (Top 50 저장 기능 제거됨)
                if (message == "!m r start")
                {
                    Console.WriteLine($"[Chat] {sender} 명령어 감지: {message}");
                    _ = _botService.ExecuteStartCommandAsync(sender, async (msg) =>
                    {
                        await SendIrcMessageAsync(sender, msg);
                    });
                }
                // [추가] 2. !m o [맵ID] [스타일] 명령어 처리
                else if (message.StartsWith("!m o "))
                {
                    // 예: "!m o 123456 jump"
                    string[] parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    // parts[0]="!m", parts[1]="o", parts[2]="123456", parts[3]="jump"
                    if (parts.Length >= 4)
                    {
                        if (int.TryParse(parts[2], out int mapId))
                        {
                            string style = parts[3];
                            Console.WriteLine($"[Chat] {sender} 맵 등록 시도: {mapId} -> {style}");

                            _ = _botService.RegisterMapStyleAsync(sender, mapId, style, async (msg) =>
                            {
                                await SendIrcMessageAsync(sender, msg);
                            });
                        }
                        else
                        {
                            _ = SendIrcMessageAsync(sender, "맵 번호는 숫자여야 합니다.");
                        }
                    }
                    else
                    {
                        _ = SendIrcMessageAsync(sender, "사용법: !m o [맵번호] [스타일]");
                    }
                }

                else if (message.StartsWith("!m r help"))
                {
                    await SendIrcMessageAsync(sender, "!m r start ▶ (최초실행시) 유저의 정보를 등록합니다. / (재실행시) 유저의 정보를 최신화합니다.");
                    await SendIrcMessageAsync(sender, "!m o [맵번호] [스타일] ▶ [맵번호]를 [스타일]에 저장합니다. (저장된 내용은 모든 유저가 추천받을 수 있습니다.)");
                    await SendIrcMessageAsync(sender, "== 사용예시 ▶ !m o 123456 tech");
                    await SendIrcMessageAsync(sender, "== 스타일 종류 ▶ Farm, Generic, Stream, Tech, Flow, FingCon, LowAR");
                }
            }
            catch { }
        }
    }
}