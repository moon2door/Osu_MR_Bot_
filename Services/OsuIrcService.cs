using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Linq; // Array.Exists 사용을 위해 추가

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

        // [이동] 스타일 목록을 이곳에서 관리합니다.
        private static readonly string[] AllowedStyles = { "farm", "generic", "stream", "tech", "flow", "fingcon", "lowAR", "DT", "Alt" };

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
                        await HandleMessage(line);
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
            else
            {
                // IRC 연결이 끊겨있거나 아직 안 된 경우 콘솔에만 출력 (콘솔 테스트용)
                Console.WriteLine($"[IRC Disconnected] To {target}: {message}");
            }
        }

        // [신규] 공용 명령어 처리기 (콘솔 & 채팅 공용)
        public async Task ProcessCommandAsync(string sender, string message)
        {
            string cmd = message.Trim();

            // 1. !m r start
            if (cmd == "!m r start")
            {
                Console.WriteLine($"[Command] {sender}: {cmd}");
                _ = _botService.ExecuteStartCommandAsync(sender, async (msg) =>
                {
                    await SendIrcMessageAsync(sender, msg);
                });
            }
            // 2. !m o [맵ID] [스타일]
            else if (cmd.StartsWith("!m o "))
            {
                string[] parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 4)
                {
                    if (int.TryParse(parts[2], out int mapId))
                    {
                        // [수정] 입력값을 소문자로 변환하지 않고 그대로 가져옵니다.
                        string inputStyle = parts[3];

                        // [수정] 스타일 유효성 검사 (대소문자 무시하고 비교)
                        // 예: "dt", "DT", "Dt" 모두 허용
                        if (Array.Exists(AllowedStyles, s => s.Equals(inputStyle, StringComparison.OrdinalIgnoreCase)))
                        {
                            Console.WriteLine($"[Command] {sender} 맵 등록 시도: {mapId} -> {inputStyle}");

                            // 봇 서비스에는 입력값 그대로 전달 (서비스 내부에서 소문자로 저장함)
                            _ = _botService.RegisterMapStyleAsync(sender, mapId, inputStyle, async (msg) =>
                            {
                                await SendIrcMessageAsync(sender, msg);
                            });
                        }
                        else
                        {
                            await SendIrcMessageAsync(sender, $"[Error] 유효하지 않은 스타일입니다. ({string.Join(", ", AllowedStyles)})");
                        }
                    }
                    else
                    {
                        await SendIrcMessageAsync(sender, "[Error] 맵 번호는 숫자여야 합니다.");
                    }
                }
                else
                {
                    await SendIrcMessageAsync(sender, "[Usage] !m o [맵번호] [스타일]");
                }
            }
            // 3. !m r help
            else if (cmd.StartsWith("!m r help"))
            {
                await SendIrcMessageAsync(sender, "=== Osu! MR Bot 도움말 ===");
                await SendIrcMessageAsync(sender, "!m r start : 봇 등록 및 정보 갱신");
                await SendIrcMessageAsync(sender, "!m o [맵ID] [스타일] : 맵 스타일 등록");
                await SendIrcMessageAsync(sender, $"└ 스타일: {string.Join(", ", AllowedStyles)}");
            }
        }

        private async Task HandleMessage(string rawLine)
        {
            try
            {
                int exclaimIndex = rawLine.IndexOf('!');
                if (exclaimIndex < 1) return;

                string sender = rawLine.Substring(1, exclaimIndex - 1);

                // 봇 자신이 보낸 메시지는 처리하지 않고 무시
                if (sender == _botUsername) return;

                int msgIndex = rawLine.IndexOf(" :", exclaimIndex);
                if (msgIndex < 0) return;

                string message = rawLine.Substring(msgIndex + 2).Trim();

                // 실제 처리는 ProcessCommandAsync에게 위임
                await ProcessCommandAsync(sender, message);
            }
            catch { }
        }
    }
}