﻿using System;
using System.Collections.Generic;
using System.Linq;
using Crosstalk.Core.Enums;
using Crosstalk.Core.Models;
using Crosstalk.Core.Models.Channels;
using Crosstalk.Core.Models.Relationships;
using MongoDB.Bson;
using Neo4jClient;
using Neo4jClient.Gremlin;

namespace Crosstalk.Core.Repositories
{
    public class EdgeRepository : BaseNeo4JRepository, IEdgeRepository
    {
        public const string Broadcast = "broadcast";
        public const string Private = "private";

        public EdgeRepository(IGraphClient client) : base(client) {}

        public IEdgeRepository Save(Edge edge, ChannelType type)
        {
            edge.Type = type;
            return this.Save(edge);
        }

        public IEdgeRepository Save(Edge edge){
            if (null == edge.Type)
            {
                throw new ArgumentNullException("edge", "Edge has no channel type");
            }
            var constructorInfo = edge.Type.ToType().GetConstructor(new Type[] {typeof (NodeReference)});
            if (constructorInfo == null)
            {   
                throw new ArgumentOutOfRangeException("edge", "Not a valid channel type");
            }
            var channel = (BaseChannel) constructorInfo.Invoke(new object[] { edge.To.GraphId });
            this.Client.CreateRelationship((NodeReference<GraphIdentity>) edge.From.GraphId, channel);
            return this;
        }

        //public Edge GetById(long id)
        //{
        //    var nodes = this.Client.RootNode.InE(id.ToString()).BothV<GraphIdentity>().ToList();
        //    return new Edge()
        //        {
        //            Id = id,
        //            From = new Identity
        //                {
        //                    Id = nodes[0].Data.Id
        //                },
        //            To = new Identity
        //                {
        //                    Id = nodes[1].Data.Id
        //                }
        //        };
        //}

        public IEnumerable<Edge> GetFromNode(Identity node, ChannelType type)
        {
            type = type ?? ChannelType.Public;
            return this.Client
                       .Get<GraphIdentity>(node.GraphId)
                       .OutE(type)
                       .Select(n => new Edge()
                       {
                           To = new Identity
                               {
                                   OId = ObjectId.Parse(this.Client.Get<GraphIdentity>(n.EndNodeReference).Data.Id),
                                   GraphId = n.EndNodeReference.Id
                               },
                           From = node,
                           Id = n.Reference.Id,
                           Type = n.TypeKey
                       });
        }

        public IEnumerable<Edge> GetToNode(Identity node, ChannelType type)
        {
            return this.GetToNode(node, type, 1);
        }

        public IEnumerable<Edge> GetToNode(Identity node, ChannelType type, uint depth)
        {
            type = type ?? ChannelType.Public;
            // g.v(317).as('x').outE.as('edge').inV.loop('x'){it.loops < 3 && it.object.Type != "public"}.path().scatter.dedup.filter{it.Id == null}.id
            var rels = this.Client.ExecuteGetAllRelationshipsGremlin<Edge>(
                "g.v(node).as('x').outE(channel).inV.loop('x'){it.loops < 3 && it.object.Type != type}.path().scatter.dedup.filter{it.Id == null}",
                new Dictionary<string, object>()
                    {
                        {"node", node.GraphId},
                        {"type", Identity.Public},
                        {"channel", type.ToString()}
                    });
            return rels.Select(n => new Edge()
                {
                    From = new Identity
                        {
                            OId = ObjectId.Parse(this.Client.Get<GraphIdentity>(n.StartNodeReference).Data.Id),
                            GraphId = n.StartNodeReference.Id
                        },
                    To = new Identity
                        {
                            Id = this.Client.Get<GraphIdentity>(n.EndNodeReference).Data.Id,
                            GraphId = n.EndNodeReference.Id
                        },
                    Id = n.Reference.Id,
                    Type = n.TypeKey
                });
            return this.Client
                       .Get<GraphIdentity>(node.GraphId)
                       //.As<GraphIdentity>("x")
                       .InE(Edges.Broadcast)
                       //.LoopV<GraphIdentity>("x", depth)
                       //.Where(v => v.Data.Type == Identity.Public)
                       //.
                       .Select(n => new Edge()
                       {
                           From = new Identity {
                               OId = ObjectId.Parse(this.Client.Get<GraphIdentity>(n.StartNodeReference).Data.Id),
                               GraphId = n.StartNodeReference.Id
                           },
                           To = node,
                           Id = n.Reference.Id
                       });
        }

        public Edge GetByFromTo(Identity @from, Identity to, ChannelType type)
        {
            var rType = type.ToType();
            return this.Client.Get<GraphIdentity>(from.GraphId)
                       .OutE(type)
                       .Where(rel => rel.EndNodeReference == to.GraphId)
                       .Select(e => new Edge
                           {
                               From = from,
                               To = to,
                               Id = e.Reference.Id,
                               Type = e.TypeKey
                           }).FirstOrDefault();
        }
    }
}