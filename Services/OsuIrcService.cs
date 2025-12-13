using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

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

        // 허용된 스타일 목록
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

        public async Task SendIrcMessageAsync(string target, string message)
        {
            if (_writer != null && _tcpClient.Connected)
            {
                await _writer.WriteLineAsync($"PRIVMSG {target} :{message}");
                Console.WriteLine($"[IRC Send] To {target}: {message}");
            }
            else
            {
                Console.WriteLine($"[IRC Disconnected] To {target}: {message}");
            }
        }

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

            // [신규] 2. !m r diff [1~4] (난이도 설정)
            else if (cmd.StartsWith("!m r diff "))
            {
                string[] parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                // parts[0]=!m, parts[1]=r, parts[2]=diff, parts[3]=number

                if (parts.Length == 4 && int.TryParse(parts[3], out int diffLevel))
                {
                    if (diffLevel >= 1 && diffLevel <= 4)
                    {
                        Console.WriteLine($"[Command] {sender} 난이도 변경: {diffLevel}");
                        _ = _botService.SetUserDifficultyAsync(sender, diffLevel, async (msg) =>
                        {
                            await SendIrcMessageAsync(sender, msg);
                        });
                    }
                    else
                    {
                        await SendIrcMessageAsync(sender, "[Error] 난이도는 1~4 사이의 숫자여야 합니다.");
                    }
                }
                else
                {
                    await SendIrcMessageAsync(sender, "[Usage] !m r diff [1(쉬움)~4(매우어려움)]");
                }
            }

            // [수정] 3. !m r req [스타일] (난이도 인자 제거)
            else if (cmd.StartsWith("!m r req "))
            {
                string[] parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                // parts[0]=!m, parts[1]=r, parts[2]=req, parts[3]=style

                if (parts.Length >= 4)
                {
                    string inputStyle = parts[3];

                    if (Array.Exists(AllowedStyles, s => s.Equals(inputStyle, StringComparison.OrdinalIgnoreCase)))
                    {
                        Console.WriteLine($"[Command] {sender} 맵 추천 요청: {inputStyle}");
                        // 난이도는 내부적으로 DB에서 조회
                        _ = _botService.RecommendMapAsync(sender, inputStyle, async (msg) =>
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
                    await SendIrcMessageAsync(sender, "[Usage] !m r req [스타일]");
                }
            }

            // 3. !m o [스타일] [맵ID...]
            else if (cmd.StartsWith("!m o "))
            {
                string[] parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 4)
                {
                    string inputStyle = parts[2];

                    if (Array.Exists(AllowedStyles, s => s.Equals(inputStyle, StringComparison.OrdinalIgnoreCase)))
                    {
                        List<int> mapIds = new List<int>();
                        bool allParsed = true;

                        for (int i = 3; i < parts.Length; i++)
                        {
                            if (int.TryParse(parts[i], out int mapId))
                            {
                                mapIds.Add(mapId);
                            }
                            else
                            {
                                allParsed = false;
                                break;
                            }
                        }

                        if (allParsed && mapIds.Count > 0)
                        {
                            if (mapIds.Count == 1)
                            {
                                Console.WriteLine($"[Command] {sender} 맵 등록 시도: {mapIds[0]} -> {inputStyle}");
                                _ = _botService.RegisterMapStyleAsync(sender, mapIds[0], inputStyle, async (msg) =>
                                {
                                    await SendIrcMessageAsync(sender, msg);
                                });
                            }
                            else
                            {
                                Console.WriteLine($"[Command] {sender} 맵 일괄 등록 시도: {mapIds.Count}개 -> {inputStyle}");
                                _ = _botService.RegisterBatchMapStylesAsync(sender, mapIds, inputStyle, async (msg) =>
                                {
                                    await SendIrcMessageAsync(sender, msg);
                                });
                            }
                        }
                        else
                        {
                            await SendIrcMessageAsync(sender, "[Error] 맵 번호 형식이 올바르지 않습니다. (모두 숫자여야 함)");
                        }
                    }
                    else
                    {
                        await SendIrcMessageAsync(sender, $"[Error] 유효하지 않은 스타일입니다. ({string.Join(", ", AllowedStyles)})");
                    }
                }
                else
                {
                    await SendIrcMessageAsync(sender, "[Usage] !m o [스타일] [맵번호] ... [맵번호]");
                }
            }

            // [신규] 4. !m r del [맵번호] (삭제 요청)
            else if (cmd.StartsWith("!m r del "))
            {
                string[] parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                // parts[0]=!m, parts[1]=r, parts[2]=del, parts[3]=mapId

                if (parts.Length == 4 && int.TryParse(parts[3], out int mapId))
                {
                    Console.WriteLine($"[Command] {sender} 삭제 요청: {mapId}");
                    _ = _botService.RequestMapDeletionAsync(sender, mapId, async (msg) =>
                    {
                        await SendIrcMessageAsync(sender, msg);
                    });
                }
                else
                {
                    await SendIrcMessageAsync(sender, "[Usage] !m r del [맵번호]");
                }
            }

            // 4. !m r help
            else if (cmd.StartsWith("!m r help"))
            {
                await SendIrcMessageAsync(sender, "=== Osu! MR Bot 명령어 도움말 ===");
                await SendIrcMessageAsync(sender, "!m r start : 봇 등록 및 정보 갱신");
                await SendIrcMessageAsync(sender, "!m r diff [1~4]: 난이도 변경");
                await SendIrcMessageAsync(sender, "└ 난이도: easy(1), normal(2), hard(3), expert(4)");
                await SendIrcMessageAsync(sender, "!m r req [스타일] : 해당 스타일에 맞는 맵 추천");
                await SendIrcMessageAsync(sender, "!m o [스타일] [맵ID] ... : 맵 스타일 등록 (여러 개 가능)");
                await SendIrcMessageAsync(sender, $"└ 스타일 종류 : {string.Join(", ", AllowedStyles)}");
                await SendIrcMessageAsync(sender, "==============================================");
                await SendIrcMessageAsync(sender, "=== 맵 추가 관련 ===");
                await SendIrcMessageAsync(sender, "플레이 했는데, 스타일과 맞지 않는 맵이 있는 경우");
                await SendIrcMessageAsync(sender, "!m r del [맵번호] 를 입력하여 주세요");
                await SendIrcMessageAsync(sender, "관리자가 확인 후 삭제하겠습니다.");
            }
        }

        private async Task HandleMessage(string rawLine)
        {
            try
            {
                int exclaimIndex = rawLine.IndexOf('!');
                if (exclaimIndex < 1) return;

                string sender = rawLine.Substring(1, exclaimIndex - 1);

                if (sender == _botUsername) return;

                int msgIndex = rawLine.IndexOf(" :", exclaimIndex);
                if (msgIndex < 0) return;

                string message = rawLine.Substring(msgIndex + 2).Trim();

                await ProcessCommandAsync(sender, message);
            }
            catch { }
        }
    }
}