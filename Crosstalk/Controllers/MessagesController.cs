﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using Crosstalk.Core.Collections;
using Crosstalk.Core.Exceptions;
using Crosstalk.Core.Models;
using Crosstalk.Core.Models.Messages;
using Crosstalk.Core.Models.Channels;
using Crosstalk.Core.Repositories;
using Crosstalk.Core.Services;
using MongoDB.Bson;
using Newtonsoft.Json.Linq;
using Crosstalk.Common.Models;
using System.Collections.Specialized;

namespace Crosstalk.Core.Controllers
{
    public class MessagesController : ApiController
    {

        private readonly IMessageRepository _messageRepository;
        private readonly IEdgeRepository _edgeRepository;
        private readonly IIdentityRepository _identityRepository;
        private readonly IMessageService _messageService;

        public MessagesController(IMessageRepository messageRepository,
            IEdgeRepository edgeRepository,
            IIdentityRepository identityRepository,
            IMessageService messageService)
        {
            this._messageRepository = messageRepository;
            this._edgeRepository = edgeRepository;
            this._identityRepository = identityRepository;
            this._messageService = messageService;
        }

        // GET api/messages?identity=id
        /// <summary>
        /// Gets all messages in the public space
        /// </summary>
        /// <returns></returns>
        public IEnumerable<Message> GetByIdentity(string identity, bool thisIsMe)
        {
            var me = this._identityRepository.GetById(ObjectId.Parse(identity));
            var edges = new List<Edge>(this._edgeRepository.GetToNode(me, ChannelType.Public, (uint) (thisIsMe ? 2 : 1)));
            var messages = new List<Message>();
            foreach (var edge in edges)
            {
                edge.From = edge.From.Id == me.Id ? me : this._identityRepository.GetById(edge.From.Id);
                edge.To = edge.To.Id == me.Id ? me : this._identityRepository.GetById(edge.To.Id);
                messages.AddRange(this._messageService.GetListForEdge(edge));
            }
            return messages;
        }

        [HttpGet]
        [ActionName("Feed")]
        public IEnumerable<Message> Feed(string id, IEnumerable<Edge> exclusions)
        {
            throw new Exception(string.Format("Endpoint deprecated use /api/feed/{0} instead", id));
            var me = this._identityRepository.GetById(id);
            var edges = new List<Edge>(this._edgeRepository.GetToNode(me, ChannelType.Public, 3))
                .Where(e => null == exclusions || !exclusions.Contains(e));
            var messages = new OrderedList<Message>((l, n) =>
                l.Created == n.Created ? 0 : l.Created > n.Created ? 1 : -1);
            Parallel.ForEach(edges, edge =>
                {
                    edge.From = edge.From.Id == me.Id ? me : this._identityRepository.GetById(edge.From.Id);
                    edge.To = edge.To.Id == me.Id ? me : this._identityRepository.GetById(edge.To.Id);
                    lock (messages)
                    {
                        messages.AddRange(this._messageService.GetListForEdge(edge));
                    }
                });
            return messages;
        }

        [HttpGet]
        public IEnumerable<Message> Channel(string from, string to, string type)
        {
            return this.Channel(from, to, type, null);
        }

        [HttpGet]
        public IEnumerable<Message> Channel(string from, string to, string type, int? count)
        {
            var fId = this._identityRepository.GetById(from);
            var tId = this._identityRepository.GetById(to);

            var edges = new Edge[2]
                {
                    this._edgeRepository.GetByFromTo(fId, tId, type),
                    this._edgeRepository.GetByFromTo(tId, fId, type)
                };

            var messages = new OrderedList<Message>((l, n) =>
                l.Created == n.Created ? 0 : l.Created > n.Created ? 1 : -1);

            foreach (var edge in edges.Where(e => null != e))
            {
                edge.From = edge.From.Id == from ? fId : tId;
                edge.To = edge.To.Id == to ? tId : fId;
                
                foreach (var message in this._messageService.GetListForEdge(edge, count))
                {
                    messages.Add(message);
                }
            }

            return messages;
        }

        // GET api/messages/:id
        /// <summary>
        /// Get a specific message
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpGet]
        [ActionName("Get")]
        public Message Get(string id)
        {
            var message = this._messageRepository.GetById(id);
            message.Edge = this._edgeRepository.GetById(message.Edge.Id);
            this._identityRepository.BindPartial(message.Edge, new[] { "From", "To" });
            return message;
        }

        [HttpGet]
        [ActionName("Search")]
        public IEnumerable<Message> Search()
        {
            var qStr = HttpUtility.ParseQueryString(this.Request.RequestUri.Query);
            var and = (null == qStr["or"]) ? true : false;
            if (null != qStr["or"])
            {
                qStr.Remove("or");
            }            
            var messages = (and) ? this._messageRepository.Search(qStr).ToList() : this._messageRepository.Search(qStr, false).ToList();
            Parallel.ForEach(messages, m =>
            {
                m.Edge = this._edgeRepository.GetById(m.Edge.Id);
                m.Edge.To = this._identityRepository.GetById(m.Edge.To.Id);
                m.Edge.From = this._identityRepository.GetById(m.Edge.From.Id);
            });
            return messages;
        }

        [HttpGet]
        [ActionName("SearchFor")]
        public IEnumerable<Message> SearchFor()
        {
            var qStr = HttpUtility.ParseQueryString(this.Request.RequestUri.Query);
            var identities = qStr.GetValues("Identity")
                .Distinct()
                .AsParallel()
                .Select(id => this._identityRepository.GetById(id))
                .ToList();
            qStr.Remove("Identity");
            var edges = new List<Edge>();
            Parallel.ForEach(identities, identity =>
            {
                foreach (var edge in this._edgeRepository.GetAllNode(identity).Where(e => ChannelType.Public == e.Type))
                {
                    lock (edges)
                    {
                        if (!edges.Any(e => e.Id == edge.Id))
                        {
                            edges.Add(edge);
                            qStr.Add("Edge._id", edge.Id.ToString());
                        }
                    }
                }
            });
            var messages = this._messageRepository.Search(qStr, false).ToList();
            var missingEdges = messages.Select(m => m.Edge.Id)
                .Distinct()
                .Where(id => !edges.Any(e => e.Id == id))
                .AsParallel()
                .Select(id => this._edgeRepository.GetById(id))
                .ToList();
            edges.AddRange(missingEdges);
            messages.AsParallel().ForAll(m => m.Edge = edges.Where(e => e.Id == m.Edge.Id).SingleOrDefault());
            var missingIdentities = messages
                .Select(m => m.Edge.From.Id)
                .Concat(messages
                .Select(m => m.Edge.To.Id))
                .Distinct()
                .Where(id => !identities.Any(i => i.Id == id))
                .AsParallel()
                .Select(id => this._identityRepository.GetById(id))
                .ToList();
            identities.AddRange(missingIdentities);
            Parallel.ForEach(messages, message =>
                {
                    message.Edge.From = identities.Where(i => i.Id == message.Edge.From.Id).SingleOrDefault();
                    message.Edge.To = identities.Where(i => i.Id == message.Edge.To.Id).SingleOrDefault();
                });
            return messages.OrderByDescending(m => m.Created);
        }

        // POST api/messages
        public Message Post(Message message)
        {
            this._messageRepository.Save(message);
            return message;
        }

        // PUT api/values/5
        public void Put(int id, [FromBody]string value)
        {
        }

        // DELETE api/values/5
        public void Delete(string id, string action)
        {
            if (ReportRevokeActionType.Revoke == action)
            {
                this._messageRepository.Revoke(id);
            }
            else if (ReportRevokeActionType.Moderate == action)
            {
                this._messageRepository.Moderate(id);
            }
        }
    }
}
