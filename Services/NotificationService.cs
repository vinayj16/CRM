using CRM.Helpers;
using CRM.Models;
using MongoDB.Driver;

namespace CRM.Services
{
    public interface INotificationService
    {
        Task CreateNotificationAsync(string title, string message, string type, int? userId = null, string? link = null, int? relatedEntityId = null, string? relatedEntityType = null, string priority = "Normal", int? tenantId = null);
        Task<List<NotificationModel>> GetUserNotificationsAsync(int userId, string userRole);
        Task<int> GetUnreadCountAsync(int userId, string userRole);
        Task MarkAsReadAsync(int notificationId);
        Task MarkAllAsReadAsync(int userId);

        // Specific notification methods
        Task NotifyLeadAddedAsync(int leadId, string leadName, string source);
        Task NotifyLeadAssignedAsync(int leadId, string leadName, int assignedToUserId, string assignedByUserName);
        Task NotifyQuotationCreatedAsync(int quotationId, int leadId, string leadName, int createdForUserId);
        Task NotifyInvoiceCreatedAsync(int invoiceId, int leadId, string leadName);
        Task NotifyFollowUpDueAsync(int followUpId, int leadId, string leadName, DateTime dueDate, int assignedToUserId);
        Task NotifyPaymentReceivedAsync(int paymentId, decimal amount, string leadName);
        Task NotifyBookingCreatedAsync(int bookingId, string propertyName, string customerName);
        Task CreateWelcomeNotificationAsync(int userId, string username);
        Task CreateTestNotificationAsync(int userId);
        Task ClearAllNotificationsAsync();
        Task NotifyPartnerHandoverAsync(int leadId, string leadName);
    }

    public class NotificationService : INotificationService
    {
        private readonly AppDbContext _context;
        private readonly FcmService _fcmService;

        public NotificationService(AppDbContext context, FcmService fcmService)
        {
            _context = context;
            _fcmService = fcmService;
        }

        public async Task CreateNotificationAsync(string title, string message, string type, int? userId = null, string? link = null, int? relatedEntityId = null, string? relatedEntityType = null, string priority = "Normal", int? tenantId = null)
        {
            // Prevent duplicate notifications: check if a similar (same type, userId, relatedEntityId) unread notification already exists
            var recentWindow = IndianTime.Now.AddMinutes(-30);
            var duplicate = await _context.Notifications
                .Where(n => n.Type == type && n.Title == title && n.UserId == userId
                    && n.RelatedEntityId == relatedEntityId
                    && !n.IsRead
                    && n.CreatedOn >= recentWindow)
                .FirstOrDefaultAsync();
            if (duplicate != null)
            {
                // Update the existing notification's timestamp so it stays at the top
                duplicate.CreatedOn = IndianTime.Now;
                duplicate.Message = message;
                duplicate.Link = link;
                duplicate.Priority = priority;
                // Also stamp the tenant on the duplicate if it was a legacy tenant-0 orphan
                if (duplicate.TenantId <= 0 && tenantId is int dupTid && dupTid > 0)
                    duplicate.TenantId = dupTid;
                await _context.SaveChangesAsync();
                return;
            }

            var notification = new NotificationModel
            {
                Title = title,
                Message = message,
                Type = type,
                UserId = userId,
                Link = link,
                RelatedEntityId = relatedEntityId,
                RelatedEntityType = relatedEntityType,
                Priority = priority,
                IsRead = false,
                CreatedOn = IndianTime.Now
            };

            // Background services (no HTTP context) cannot resolve the tenant from the
            // request, so callers that know their tenant pass it explicitly. This prevents
            // TenantId=0 orphaned notifications.
            if (tenantId is int tid && tid > 0)
                notification.TenantId = tid;

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            // Send real-time push notification via Firebase
            await SendRealTimeNotificationAsync(notification);
        }

        public async Task<List<NotificationModel>> GetUserNotificationsAsync(int userId, string userRole)
        {
            var query = _context.Notifications.AsQueryable();

            if (userRole == "Admin")
            {
                query = query.Where(n => n.UserId == null || n.UserId == userId);
            }
            else
            {
                query = query.Where(n => n.UserId == userId);
            }

            return await query
                .OrderByDescending(n => n.CreatedOn)
                .Take(50)
                .ToListAsync();
        }

        public async Task<int> GetUnreadCountAsync(int userId, string userRole)
        {
            var query = _context.Notifications.AsQueryable();

            if (userRole == "Admin")
            {
                query = query.Where(n => n.UserId == null || n.UserId == userId);
            }
            else
            {
                query = query.Where(n => n.UserId == userId);
            }

            return await query.Where(n => !n.IsRead).CountAsync();
        }

        public async Task MarkAsReadAsync(int notificationId)
        {
            var notification = await _context.Notifications.FindAsync(notificationId);
            if (notification != null)
            {
                notification.IsRead = true;
                notification.ReadOn = IndianTime.Now;
                _context.Notifications.Update(notification);
                await _context.SaveChangesAsync();
            }
        }

        public async Task MarkAllAsReadAsync(int userId)
        {
            // Get user role to determine which notifications to mark as read
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return;

            var notifications = new List<NotificationModel>();

            if (user.Role == "Admin")
            {
                // Admin sees both their specific notifications AND global notifications (UserId == null)
                notifications = await _context.Notifications
                    .Where(n => (n.UserId == userId || n.UserId == null) && !n.IsRead)
                    .ToListAsync();
            }
            else
            {
                // Sales/Agent only see their specific notifications
                notifications = await _context.Notifications
                    .Where(n => n.UserId == userId && !n.IsRead)
                    .ToListAsync();
            }

            foreach (var notification in notifications)
            {
                notification.IsRead = true;
                notification.ReadOn = IndianTime.Now;
                _context.Notifications.Update(notification);
            }

            await _context.SaveChangesAsync();
        }

        // Specific notification implementations
        public async Task NotifyLeadAddedAsync(int leadId, string leadName, string source)
        {
            var lead = await _context.Leads.FindAsync(leadId);
            if (lead == null) return;

            // Only notify admins for their own leads (not partner leads)
            if (lead.ChannelPartnerId == null)
            {
                var admins = await _context.Users
                    .Where(u => u.Role == "Admin" && u.ChannelPartnerId == null)
                    .ToListAsync();

                foreach (var admin in admins)
                {
                    await CreateNotificationAsync(
                        title: "New Lead Added",
                        message: $"A new lead '{leadName}' has been added from {source}",
                        type: NotificationType.LeadAdded,
                        userId: admin.UserId,
                        //link: $"/Leads/Details/{leadId}",
                        link: $"/leaddetails/{IdObfuscator.Encode(leadId)}",
                        relatedEntityId: leadId,
                        relatedEntityType: "Lead",
                        priority: "Normal"
                    );
                }
            }
        }

        public async Task NotifyLeadAssignedAsync(int leadId, string leadName, int assignedToUserId, string assignedByUserName)
        {
            // DEBUG: Log the assignment details
            Console.WriteLine($"DEBUG NotifyLeadAssignedAsync: LeadId={leadId}, AssignedToUserId={assignedToUserId}, AssignedBy={assignedByUserName}");

            // Verify the user exists before creating notification
            var assignedUser = await _context.Users.FindAsync(assignedToUserId);
            if (assignedUser == null)
            {
                Console.WriteLine($"DEBUG ERROR: User not found for UserId={assignedToUserId}");
                return;
            }

            Console.WriteLine($"DEBUG: Creating notification for user - UserId: {assignedUser.UserId}, Username: {assignedUser.Username}, Role: {assignedUser.Role}");

            await CreateNotificationAsync(
                title: "Lead Assigned to You",
                message: $"Lead '{leadName}' has been assigned to you by {assignedByUserName}",
                type: NotificationType.LeadAssigned,
                userId: assignedToUserId,
                //link: $"/Leads/Details/{leadId}",
                link: $"/leaddetails/{IdObfuscator.Encode(leadId)}",
                relatedEntityId: leadId,
                relatedEntityType: "Lead",
                priority: "High"
            );

            Console.WriteLine($"DEBUG: Notification successfully created for UserId={assignedToUserId}");
        }

        public async Task NotifyQuotationCreatedAsync(int quotationId, int leadId, string leadName, int createdForUserId)
        {
            // Notify admin
            var admins = await _context.Users.Where(u => u.Role == "Admin").ToListAsync();
            foreach (var admin in admins)
            {
                await CreateNotificationAsync(
                    title: "New Quotation Created",
                    message: $"A quotation has been created for lead '{leadName}'",
                    type: NotificationType.QuotationCreated,
                    userId: admin.UserId,
                    link: $"/quotationdetails/{IdObfuscator.Encode(quotationId)}",
                    relatedEntityId: quotationId,
                    relatedEntityType: "Quotation",
                    priority: "Normal"
                );
            }

            // Notify the assigned sales person
            if (createdForUserId > 0)
            {
                await CreateNotificationAsync(
                    title: "Quotation Created for Your Lead",
                    message: $"A quotation has been created for your lead '{leadName}'",
                    type: NotificationType.QuotationCreated,
                    userId: createdForUserId,
                    link: $"/quotationdetails/{IdObfuscator.Encode(quotationId)}",
                    relatedEntityId: quotationId,
                    relatedEntityType: "Quotation",
                    priority: "Normal"
                );
            }
        }

        public async Task NotifyInvoiceCreatedAsync(int invoiceId, int leadId, string leadName)
        {
            // Get the lead to find assigned user
            var lead = await _context.Leads.FindAsync(leadId);

            // Notify admin
            var admins = await _context.Users.Where(u => u.Role == "Admin").ToListAsync();
            foreach (var admin in admins)
            {
                await CreateNotificationAsync(
                    title: "New Invoice Created",
                    message: $"An invoice has been created for lead '{leadName}'",
                    type: NotificationType.InvoiceCreated,
                    userId: admin.UserId,
                    link: $"/invoicedetails/{IdObfuscator.Encode(invoiceId)}",
                    relatedEntityId: invoiceId,
                    relatedEntityType: "Invoice",
                    priority: "Normal"
                );
            }

            // Notify assigned user
            if (lead?.ExecutiveId != null)
            {
                await CreateNotificationAsync(
                    title: "Invoice Created for Your Lead",
                    message: $"An invoice has been created for your lead '{leadName}'",
                    type: NotificationType.InvoiceCreated,
                    userId: lead.ExecutiveId,
                    link: $"/invoicedetails/{IdObfuscator.Encode(invoiceId)}",
                    relatedEntityId: invoiceId,
                    relatedEntityType: "Invoice",
                    priority: "Normal"
                );
            }
        }

        public async Task NotifyFollowUpDueAsync(int followUpId, int leadId, string leadName, DateTime dueDate, int assignedToUserId)
        {
            var timeUntilDue = dueDate - IndianTime.Now;
            var urgency = timeUntilDue.TotalHours < 24 ? "Urgent" : "High";

            await CreateNotificationAsync(
                title: "Follow-up Due",
                message: $"Follow-up for lead '{leadName}' is due on {dueDate:dd/MM/yyyy HH:mm}",
                type: NotificationType.FollowUpDue,
                userId: assignedToUserId,
                //link: $"/Leads/Details/{leadId}#followups",
                link: $"/leaddetails/{IdObfuscator.Encode(leadId)}#scrollspyFollowups",
                relatedEntityId: followUpId,
                relatedEntityType: "FollowUp",
                priority: urgency
            );
        }

        public async Task NotifyPaymentReceivedAsync(int paymentId, decimal amount, string leadName)
        {
            // Notify all admins
            var admins = await _context.Users.Where(u => u.Role == "Admin").ToListAsync();
            foreach (var admin in admins)
            {
                await CreateNotificationAsync(
                    title: "Payment Received",
                    message: $"Payment of ₹{amount:N2} received for '{leadName}'",
                    type: NotificationType.PaymentReceived,
                    userId: admin.UserId,
                    link: $"/paymentreceipt/{IdObfuscator.Encode(paymentId)}",
                    relatedEntityId: paymentId,
                    relatedEntityType: "Payment",
                    priority: "Normal"
                );
            }
        }

        public async Task NotifyBookingCreatedAsync(int bookingId, string propertyName, string customerName)
        {
            // Notify all admins
            var admins = await _context.Users.Where(u => u.Role == "Admin").ToListAsync();
            foreach (var admin in admins)
            {
                await CreateNotificationAsync(
                    title: "New Booking Created",
                    message: $"New booking for '{propertyName}' by {customerName}",
                    type: NotificationType.BookingCreated,
                    userId: admin.UserId,
                    link: $"/bookingdetails/{IdObfuscator.Encode(bookingId)}",
                    relatedEntityId: bookingId,
                    relatedEntityType: "Booking",
                    priority: "High"
                );
            }
        }

        public async Task CreateWelcomeNotificationAsync(int userId, string username)
        {
            await CreateNotificationAsync(
                title: "Welcome to CRM!",
                message: $"Welcome {username}! You now have access to the notification system. You'll receive notifications for new leads, assignments, and follow-up reminders.",
                type: NotificationType.SystemAlert,
                userId: userId,
                link: "/home",
                priority: "Normal"
            );
        }

        public async Task CreateTestNotificationAsync(int userId)
        {
            await CreateNotificationAsync(
                title: "Test Notification",
                message: "This is a test notification to verify the system is working correctly. Click to dismiss.",
                type: NotificationType.SystemAlert,
                userId: userId,
                link: "/home", // Changed from "#" to a valid relative URL
                priority: "Low"
            );
        }

        public async Task ClearAllNotificationsAsync()
        {
            // MongoDB: delete all notifications
            await _context.Notifications.Collection.DeleteManyAsync(FilterDefinition<NotificationModel>.Empty);
        }

        public async Task NotifyPartnerHandoverAsync(int leadId, string leadName)
        {
            // Notify only admins when partner hands over a lead
            var admins = await _context.Users
                .Where(u => u.Role == "Admin" && u.ChannelPartnerId == null)
                .ToListAsync();

            foreach (var admin in admins)
            {
                await CreateNotificationAsync(
                    title: "Partner Lead Ready to Book",
                    message: $"Partner lead '{leadName}' is ready to book and needs assignment",
                    type: "LeadHandover",
                    userId: admin.UserId,
                    link: $"/WebhookLeads/Index?handoverStatus=ReadyToBook",
                    relatedEntityId: leadId,
                    relatedEntityType: "Lead",
                    priority: "High"
                );
            }
        }

        private async Task SendRealTimeNotificationAsync(NotificationModel notification)
        {
            try
            {
                if (notification.UserId.HasValue)
                {
                    await _fcmService.SendNotificationToUser(
                        notification.UserId.Value,
                        notification.Title,
                        notification.Message,
                        notification.Link,
                        notification.Type,
                        notification.RelatedEntityId
                    );
                }
                else
                {
                    var admins = await _context.Users.Where(u => u.Role == "Admin").ToListAsync();
                    foreach (var admin in admins)
                    {
                        await _fcmService.SendNotificationToUser(
                            admin.UserId,
                            notification.Title,
                            notification.Message,
                            notification.Link,
                            notification.Type,
                            notification.RelatedEntityId
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Failed to send Firebase notification: {ex.Message}");
            }
        }
    }
}
