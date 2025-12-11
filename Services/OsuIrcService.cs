using System.Net.Sockets;
using System.Text;

namespace Osu_MR_Bot.Services
{
    public class OsuIrcService
    {
        private readonly string _botUsername;
        private readonly string _ircPassword;
        private readonly OsuBotService _botService; // 명령어 실행을 위해 필요

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
                Console.WriteLine("[IRC] 채팅 서버 연결 중...");
                _tcpClient = new TcpClient("irc.osu.ppy.sh", 6667);
                var stream = _tcpClient.GetStream();

                _reader = new StreamReader(stream, Encoding.UTF8);
                _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                // IRC 로그인 절차
                // 1. 비밀번호 전송
                await _writer.WriteLineAsync($"PASS {_ircPassword}");
                // 2. 닉네임 전송
                await _writer.WriteLineAsync($"NICK {_botUsername}");

                Console.WriteLine("[IRC] 연결 성공! 메시지 대기 중...");

                // 무한 루프로 메시지 수신 대기
                while (_tcpClient.Connected)
                {
                    string line = await _reader.ReadLineAsync();
                    if (line == null) break;

                    // 핑퐁 처리 (필수: 서버가 PING을 보내면 PONG으로 답해야 연결이 안 끊김)
                    if (line.StartsWith("PING"))
                    {
                        string pong = line.Replace("PING", "PONG");
                        await _writer.WriteLineAsync(pong);
                        // Console.WriteLine("[IRC] Ping-Pong"); // 너무 시끄러우면 주석 처리
                        continue;
                    }

                    // 채팅 메시지 처리 (PRIVMSG)
                    // 예시 포맷: :SenderName!irc_id@osu.ppy.sh PRIVMSG Target :Message Content
                    if (line.Contains(" PRIVMSG "))
                    {
                        HandleMessage(line);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IRC] 오류 발생: {ex.Message}");
            }
        }

        private void HandleMessage(string rawLine)
        {
            try
            {
                // 1. 보낸 사람(Username) 추출
                // :Username! ... 형태
                int exclaimIndex = rawLine.IndexOf('!');
                if (exclaimIndex < 1) return;

                string sender = rawLine.Substring(1, exclaimIndex - 1);

                // 2. 메시지 내용 추출
                // PRIVMSG Target :실제내용 ... 형태
                int msgIndex = rawLine.IndexOf(" :", exclaimIndex);
                if (msgIndex < 0) return;

                string message = rawLine.Substring(msgIndex + 2).Trim();

                // 3. 명령어 감지 (!m r start)
                if (message == "!m r start")
                {
                    Console.WriteLine($"[Chat] {sender}님이 명령어를 입력했습니다: {message}");

                    // 여기서 봇 서비스의 저장 로직 실행!
                    // await를 안 쓰는 이유: 채팅 수신 루프를 멈추지 않기 위해 (Fire-and-forget)
                    _ = _botService.ExecuteStartCommandAsync(sender);
                }
            }
            catch
            {
                // 파싱 에러 무시
            }
        }
    }
}