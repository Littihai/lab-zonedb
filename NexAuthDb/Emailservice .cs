//using MailKit.Net.Smtp;
//using MailKit.Security;
//using Microsoft.Extensions.Logging;
//using Microsoft.Extensions.Options;
//using MimeKit;

//namespace NexAuth.Infrastructure.Services
//{
//    public class EmailOptions
//    {
//        public const string Section = "Email";
//        public string SmtpHost     { get; set; } = string.Empty;
//        public int    SmtpPort     { get; set; } = 587;
//        public string Username     { get; set; } = string.Empty;
//        public string Password     { get; set; } = string.Empty;
//        public string FromAddress  { get; set; } = string.Empty;
//        public string FromName     { get; set; } = "NexAuth";
//    }

//    public class EmailService : IEmailService
//    {
//        private readonly EmailOptions          _opts;
//        private readonly ILogger<EmailService> _log;

//        public EmailService(IOptions<EmailOptions> opts, ILogger<EmailService> log)
//        {
//            _opts = opts.Value;
//            _log  = log;
//        }

//        public async Task SendPasswordResetEmailAsync(
//            string toEmail, string resetLink, CancellationToken ct = default)
//        {
//            var message = new MimeMessage();
//            message.From.Add(new MailboxAddress(_opts.FromName, _opts.FromAddress));
//            message.To.Add(MailboxAddress.Parse(toEmail));
//            message.Subject = "รีเซ็ตรหัสผ่าน NexAuth";

//            message.Body = new BodyBuilder
//            {
//                HtmlBody = $"""
//                    <!DOCTYPE html>
//                    <html lang="th">
//                    <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
//                    <body style="margin:0;padding:0;background:#f7f7f5;font-family:system-ui,sans-serif">
//                      <table width="100%" cellpadding="0" cellspacing="0" style="padding:40px 20px">
//                        <tr><td align="center">
//                          <table width="480" cellpadding="0" cellspacing="0"
//                            style="background:#fff;border-radius:12px;border:0.5px solid #e0e0e0;overflow:hidden">

//                            <!-- Header -->
//                            <tr>
//                              <td style="background:#111;padding:24px 32px">
//                                <p style="margin:0;font-size:20px;font-weight:500;color:#fff;letter-spacing:-0.5px">NexAuth</p>
//                              </td>
//                            </tr>

//                            <!-- Body -->
//                            <tr>
//                              <td style="padding:32px">
//                                <p style="margin:0 0 8px;font-size:18px;font-weight:500;color:#111">รีเซ็ตรหัสผ่าน</p>
//                                <p style="margin:0 0 24px;font-size:14px;color:#666;line-height:1.6">
//                                  เราได้รับคำขอรีเซ็ตรหัสผ่านสำหรับบัญชี <strong>{toEmail}</strong><br>
//                                  กดปุ่มด้านล่างเพื่อตั้งรหัสผ่านใหม่ ลิงก์จะหมดอายุใน <strong>15 นาที</strong>
//                                </p>
//                                <table cellpadding="0" cellspacing="0">
//                                  <tr>
//                                    <td style="border-radius:8px;background:#111">
//                                      <a href="{resetLink}"
//                                        style="display:inline-block;padding:12px 28px;font-size:14px;font-weight:500;color:#fff;text-decoration:none">
//                                        ตั้งรหัสผ่านใหม่ →
//                                      </a>
//                                    </td>
//                                  </tr>
//                                </table>
//                                <p style="margin:24px 0 0;font-size:12px;color:#aaa;line-height:1.6">
//                                  หากคุณไม่ได้ร้องขอ ให้เพิกเฉยต่ออีเมลนี้ รหัสผ่านจะไม่ถูกเปลี่ยน<br>
//                                  ลิงก์นี้ใช้ได้เพียงครั้งเดียวและจะหมดอายุใน 15 นาที
//                                </p>
//                              </td>
//                            </tr>

//                            <!-- Footer -->
//                            <tr>
//                              <td style="padding:16px 32px;border-top:0.5px solid #f0f0f0;background:#fafaf8">
//                                <p style="margin:0;font-size:11px;color:#bbb">
//                                  © 2025 NexAuth · ส่งโดยอัตโนมัติ กรุณาอย่าตอบกลับอีเมลนี้
//                                </p>
//                              </td>
//                            </tr>

//                          </table>
//                        </td></tr>
//                      </table>
//                    </body>
//                    </html>
//                    """,
//                TextBody = $"รีเซ็ตรหัสผ่าน NexAuth\n\nคลิกลิงก์นี้เพื่อตั้งรหัสผ่านใหม่:\n{resetLink}\n\nลิงก์หมดอายุใน 15 นาที"
//            }.ToMessageBody();

//            using var smtp = new SmtpClient();
//            try
//            {
//                await smtp.ConnectAsync(_opts.SmtpHost, _opts.SmtpPort, SecureSocketOptions.StartTls, ct);
//                await smtp.AuthenticateAsync(_opts.Username, _opts.Password, ct);
//                await smtp.SendAsync(message, ct);
//                await smtp.DisconnectAsync(true, ct);

//                _log.LogInformation("[Email] Sent reset link to {Email}", toEmail);
//            }
//            catch (Exception ex)
//            {
//                _log.LogError(ex, "[Email] Failed to send to {Email}", toEmail);
//                throw; // ให้ caller จัดการ — ไม่ swallow error
//            }
//        }
//    }
//}