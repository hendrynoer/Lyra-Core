﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lyra.Core.Accounts.Node;
using Lyra.Core.Blocks;
using Lyra.Core.Blocks.Transactions;
using Lyra.Authorizer.Services;
using Lyra.Authorizer.Decentralize;

namespace Lyra.Authorizer.Authorizers
{
    public class ExchangingAuthorizer : SendTransferAuthorizer
    {
        public ExchangingAuthorizer(ApiService apiService, ServiceAccount serviceAccount, IAccountCollection accountCollection) 
            : base(apiService, serviceAccount, accountCollection)
        {

        }

        protected override APIResultCodes ValidateFee(TransactionBlock block)
        {
            if (block.FeeType != AuthorizationFeeTypes.Regular)
                return APIResultCodes.InvalidFeeAmount;

            if (block.Fee != ExchangingBlock.FEE)
                return APIResultCodes.InvalidFeeAmount;

            return APIResultCodes.Success;
        }
    }
}