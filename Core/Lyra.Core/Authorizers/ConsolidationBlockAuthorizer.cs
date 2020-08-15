﻿using System;
using System.Collections.Generic;
using Lyra.Core.Blocks;
using Lyra.Core.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Lyra.Core.Utils;
using Lyra.Core.Accounts;
using Clifton.Blockchain;
using System.Linq;
using Lyra.Core.API;

namespace Lyra.Core.Authorizers
{
    public class ConsolidationBlockAuthorizer : BaseAuthorizer
    {
        public ConsolidationBlockAuthorizer()
        {
        }

        public async override Task<(APIResultCodes, AuthorizationSignature)> AuthorizeAsync<T>(DagSystem sys, T tblock)
        {
            var result = await AuthorizeImplAsync(sys, tblock);
            if (APIResultCodes.Success == result)
                return (APIResultCodes.Success, Sign(sys, tblock));
            else
                return (result, (AuthorizationSignature)null);
        }

        protected override Task<APIResultCodes> ValidateFeeAsync(DagSystem sys, TransactionBlock block)
        {
            return Task.FromResult(APIResultCodes.Success);
        }

        private async Task<APIResultCodes> AuthorizeImplAsync<T>(DagSystem sys, T tblock)
        {
            if (!(tblock is ConsolidationBlock))
                return APIResultCodes.InvalidBlockType;

            var block = tblock as ConsolidationBlock;

            //// 1. check if the block already exists
            //if (null != await DagSystem.Singleton.Storage.GetBlockByUIndexAsync(block.UIndex))
            //    return APIResultCodes.BlockWithThisUIndexAlreadyExists;

            var lastCons = await sys.Storage.GetLastConsolidationBlockAsync();
            if(block.Height > 1)
            {
                if (lastCons == null)
                    return APIResultCodes.CouldNotFindLatestBlock;
            }

            var result = await VerifyBlockAsync(sys, block, lastCons);
            if (result != APIResultCodes.Success)
                return result;

            // recalculate merkeltree
            // use merkle tree to consolidate all previous blocks, from lastCons.UIndex to consBlock.UIndex -1
            var mt = new MerkleTree();
            decimal feeAggregated = 0;
            foreach (var hash in block.blockHashes)
            {
                mt.AppendLeaf(MerkleHash.Create(hash));

                // aggregate fees
                var transBlock = (await sys.Storage.FindBlockByHashAsync(hash)) as TransactionBlock;
                if (transBlock != null)
                {
                    feeAggregated += transBlock.Fee;
                }
            }

            var mkhash = mt.BuildTree().ToString();
            if(block.MerkelTreeHash != mkhash)
                return APIResultCodes.InvalidConsolidationMerkleTreeHash;

            if (block.totalFees != feeAggregated.ToBalanceLong())
                return APIResultCodes.InvalidConsolidationTotalFees;

            // consolidation must come from leader node
                        

            return APIResultCodes.Success;
        }

    }
}