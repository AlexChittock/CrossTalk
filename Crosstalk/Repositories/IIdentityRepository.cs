﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Crosstalk.Common.Models;
using Crosstalk.Core.Models;
using MongoDB.Bson;
using Crosstalk.Common;

namespace Crosstalk.Core.Repositories
{
    public interface IIdentityRepository : IPartialResolver<Identity>
    {
        IIdentityRepository Save(Identity identity);
        Identity GetById(ObjectId id);
        Identity GetByGraphId(long id);
        Identity GetPublicSpace();
        IEnumerable<TItem> BindPartials<TItem>(IEnumerable<TItem> items, IEnumerable<string> fields);
        TItem BindPartial<TItem>(TItem item, IEnumerable<string> fields);
        IEnumerable<Identity> Filter(Func<Identity, bool> selector);
        IEnumerable<Identity> Search(string field, string value);
        IEnumerable<Identity> Search(NameValueCollection parameters);
    }
}
