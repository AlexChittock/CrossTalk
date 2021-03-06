﻿using Autofac;
using Crosstalk.Common.Models;
using Crosstalk.Core.Models.Messages;
using Crosstalk.Core.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace Crosstalk.Core.Services
{
    public class ReportService : IReportService
    {
        private readonly IReportRepository _reportRepository;
        private readonly IComponentContext _context;

        public ReportService(IComponentContext context)
        {
            this._reportRepository = context.Resolve<IReportRepository>();
            this._context = context;
        }

        public void Report(Report report)
        {
            this._reportRepository.Save(report);
            if (ReportType.Message == report.Type)
            {
                this._context.Resolve<IMessageRepository>().Report(report.Parent.Id);
            }
            else if (ReportType.Comment == report.Type)
            {
                this._context.Resolve<ICommentRepository>().Report(report.Parent.Id);
            }
            else
            {
                throw new ArgumentOutOfRangeException("Report.Type not recognised", "report");
            }
        }

        public void Ignore(string id)
        {
            var report = this._reportRepository.GetById(id);
            if (ReportType.Message == report.Type)
            {
                var reps = this._reportRepository.GetReportsForParent(report.Type, report.Parent.Id);
                if (reps.Count() == 1)
                {
                    var repo = this._context.Resolve<IMessageRepository>();
                    var msg = repo.GetById(report.Parent.Id);
                    msg.Status = ReportableStatus.None;
                    repo.Save(msg);
                }
            }
            this._reportRepository.Delete(id);
        }

        public void Remove(string id)
        {
            var report = this._reportRepository.GetById(id);
            if (ReportType.Message == report.Type)
            {
                this._context.Resolve<IMessageRepository>().Moderate(report.Parent.Id);
            }
        }

        public IEnumerable<IReportable> All()
        {
            IEnumerable<IReportable> messages = null;
            IEnumerable<IReportable> comments = null;

            Parallel.Invoke(
                () =>
                {
                    messages = this._context.Resolve<IMessageRepository>()
                        .Search(new System.Collections.Specialized.NameValueCollection {
                                { "Status", ReportableStatus.Reported }
                            }).ToList();
                    Parallel.ForEach<IReportable>(messages, m =>
                        {
                            m.Reports = this._reportRepository.GetReportsForParent(ReportType.Message, m.Id);
                        });
                },
                () =>
                {
                    comments = this._context.Resolve<ICommentRepository>()
                        .Search(new System.Collections.Specialized.NameValueCollection {
                                { "Status", ReportableStatus.Reported }
                            }).ToList();
                    Parallel.ForEach<IReportable>(comments, c =>
                        {
                            c.Reports = this._reportRepository.GetReportsForParent(ReportType.Comment, c.Id);
                        });
                });
            return messages.Union(comments);
        }
    }
}