using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class LeadNoteModel
    {
        public int TenantId { get; set; } = 0;

        [Key]

        public int NoteId { get; set; }
        public int LeadId { get; set; }
        public string NoteText { get; set; }
        public int? ExecutiveId { get; set; }
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
    }
}
