using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ThemeBot
{
    public class LUser
    {
        /// <summary>
        /// Users Telegram ID
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// What question did we ask the user
        /// </summary>
        public QuestionType QuestionAsked { get; set; } = QuestionType.None;
        /// <summary>
        /// Results of their last search
        /// </summary>
        public List<Theme> ResultSet { get; set; } = new List<Theme>();

        /// <summary>
        /// What page of the results are they on?
        /// </summary>
        public int Page { get; set; } = 0;
        public Theme ThemeCreating { get; set; }
        public string Search { get; set; }
        public Theme ThemeUpdating { get; set; }
    }
}
