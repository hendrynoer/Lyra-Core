using System;
using System.Collections.Generic;
using Lyra.Core.Blocks;
using Lyra.Core.Blocks.Fees;
using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Bson.Serialization.Options;
using System.Linq;
using Lyra.Core.Utils;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Core.Authorizers;
using Lyra.Core.API;
//using Javax.Security.Auth;

namespace Lyra.Core.Accounts
{
    // this is account collection (collection of block chains) used on the node side only
    // 
    public class MongoAccountCollection : IAccountCollectionAsync
    {
        //private const string COLLECTION_DATABASE_NAME = "account_collection";
        private LyraConfig _config;

        private MongoClient _Client;

        private IMongoCollection<Block> _blocks;

        readonly string _blocksCollectionName;

        IMongoDatabase _db;

        readonly string _DatabaseName;

        ILogger _log;

        public string Cluster { get; set; }

        public MongoAccountCollection()
        {
            _log = new SimpleLogger("Mongo").Logger;

            _config = Neo.Settings.Default.LyraNode;

            _DatabaseName = _config.Lyra.Database.DatabaseName;

            _blocksCollectionName = $"{LyraNodeConfig.GetNetworkId()}_blocks";

            BsonSerializer.RegisterSerializer(typeof(DateTime), new DateTimeSerializer(DateTimeKind.Utc, BsonType.Document));

            BsonClassMap.RegisterClassMap<Block>(cm =>
            {
                cm.AutoMap();
                cm.SetIsRootClass(true);
            });

            BsonClassMap.RegisterClassMap<TransactionBlock>();
            BsonClassMap.RegisterClassMap<SendTransferBlock>();
            BsonClassMap.RegisterClassMap<ExchangingBlock>();
            BsonClassMap.RegisterClassMap<ReceiveTransferBlock>();
            BsonClassMap.RegisterClassMap<OpenWithReceiveTransferBlock>();
            BsonClassMap.RegisterClassMap<LyraTokenGenesisBlock>();
            BsonClassMap.RegisterClassMap<TokenGenesisBlock>();
            BsonClassMap.RegisterClassMap<TradeBlock>();
            BsonClassMap.RegisterClassMap<TradeOrderBlock>();
            BsonClassMap.RegisterClassMap<ExecuteTradeOrderBlock>();
            BsonClassMap.RegisterClassMap<CancelTradeOrderBlock>();
            BsonClassMap.RegisterClassMap<ReceiveAuthorizerFeeBlock>();
            BsonClassMap.RegisterClassMap<ConsolidationBlock>();
            BsonClassMap.RegisterClassMap<ServiceBlock>();
            BsonClassMap.RegisterClassMap<AuthorizationSignature>();
            BsonClassMap.RegisterClassMap<NullTransactionBlock>();
            BsonClassMap.RegisterClassMap<ImportAccountBlock>();
            BsonClassMap.RegisterClassMap<OpenAccountWithImportBlock>();

            _blocks = GetDatabase().GetCollection<Block>(_blocksCollectionName);

            Cluster = GetDatabase().Client.Cluster.ToString();

            async Task CreateIndexes(string columnName, bool uniq)
            {
                try
                {
                    var options = new CreateIndexOptions() { Unique = uniq };
                    var field = new StringFieldDefinition<Block>(columnName);
                    var indexDefinition = new IndexKeysDefinitionBuilder<Block>().Ascending(field);
                    var indexModel = new CreateIndexModel<Block>(indexDefinition, options);
                    await _blocks.Indexes.CreateOneAsync(indexModel);
                }
                catch(Exception ex)
                {
                    await _blocks.Indexes.DropOneAsync(columnName + "_1");
                    await CreateIndexes(columnName, uniq);
                }
            }

            async Task CreateNoneStringIndex(string colName, bool uniq)
            {
                try
                {
                    var options = new CreateIndexOptions() { Unique = uniq };
                    IndexKeysDefinition<Block> keyCode = "{ " + colName + ": 1 }";
                    var codeIndexModel = new CreateIndexModel<Block>(keyCode, options);
                    await _blocks.Indexes.CreateOneAsync(codeIndexModel);

                }
                catch (Exception ex)
                {
                    await _blocks.Indexes.DropOneAsync(colName + "_1");
                    await CreateIndexes(colName, uniq);
                }
            }

            CreateIndexes("_t", false).Wait();
            CreateIndexes("Hash", true).Wait();
            CreateIndexes("TimeStamp", false).Wait();
            CreateIndexes("TimeStamp.Ticks", false).Wait();
            CreateIndexes("PreviousHash", false).Wait();
            CreateIndexes("AccountID", false).Wait();
            CreateNoneStringIndex("Height", false).Wait();
            CreateNoneStringIndex("BlockType", false).Wait();

            CreateIndexes("SourceHash", false).Wait();
            CreateIndexes("DestinationAccountId", false).Wait();
            CreateIndexes("Ticker", false).Wait();
            CreateIndexes("VoteFor", false).Wait();

            CreateNoneStringIndex("OrderType", false).Wait();
            CreateIndexes("SellTokenCode", false).Wait();
            CreateIndexes("BuyTokenCode", false).Wait();
            CreateIndexes("TradeOrderId", false).Wait();

            CreateIndexes("ImportedAccountId", false).Wait();
        }

        /// <summary>
        /// Deletes all blocks and the block collection
        /// </summary>
        public void Delete()
        {
            if (GetClient() == null)
                return;

            if (GetDatabase() == null)
                return;

            GetDatabase().DropCollection(_blocksCollectionName);
        }

        private MongoClient GetClient()
        {
            if (_Client == null)
                _Client = new MongoClient(_config.Lyra.Database.DBConnect);
            return _Client;
        }

        private IMongoDatabase GetDatabase()
        {
            if (_db == null)
                _db = GetClient().GetDatabase(_DatabaseName);
            return _db;
        }

        public async Task<long> GetBlockCountAsync()
        {
            return await _blocks.CountDocumentsAsync(new BsonDocument());
        }

        public async Task<long> GetBlockCountAsync(string AccountId)
        {
            var filter = Builders<Block>.Filter.Eq("AccountID", AccountId);
            var result = await _blocks.CountDocumentsAsync(filter);

            return result;
        }

        public async Task<bool> AccountExistsAsync(string AccountId)
        {
            var options = new FindOptions<Block, Block>
            {
                Limit = 1
            };

            var filter = Builders<Block>.Filter.Eq("AccountID", AccountId);
            var result = await _blocks.FindAsync(filter, options);
            return await result.AnyAsync();
        }

        public async Task<ServiceBlock> GetLastServiceBlockAsync()
        {
            var options = new FindOptions<Block, Block>
            {
                Limit = 1,
                Sort = Builders<Block>.Sort.Descending(o => o.Height)
            };
            var filter = Builders<Block>.Filter;
            var filterDefination = filter.Eq("BlockType", BlockTypes.Service);

            var finds = await _blocks.FindAsync(filterDefination, options);
            return await finds.FirstOrDefaultAsync() as ServiceBlock;
        }

        public async Task<ConsolidationBlock> GetLastConsolidationBlockAsync()
        {
            var options = new FindOptions<Block, Block>
            {
                Limit = 1,
                Sort = Builders<Block>.Sort.Descending(o => o.Height)
            };
            var filter = Builders<Block>.Filter.Eq("BlockType", BlockTypes.Consolidation);

            var finds = await _blocks.FindAsync(filter, options);
            var result = await finds.FirstOrDefaultAsync();
            return result as ConsolidationBlock;
        }

        // max 30
        public async Task<List<ConsolidationBlock>> GetConsolidationBlocksAsync(long startHeight, int count)
        {
            var options = new FindOptions<ConsolidationBlock, ConsolidationBlock>
            {
                Limit = count > 30 ? 30 : count,
                Sort = Builders<ConsolidationBlock>.Sort.Ascending(o => o.Height)
            };
            var builder = Builders<ConsolidationBlock>.Filter;
            var filterDefinition = builder.And(builder.Eq("BlockType", BlockTypes.Consolidation),
                builder.Gte("Height", startHeight));

            var result = await _blocks.OfType<ConsolidationBlock>()
                .FindAsync(filterDefinition, options);
            return result.ToList();
        }

        public async Task<List<ConsolidationBlock>> GetConsolidationBlocksAsync(string belongToSvcHash)
        {
            var options = new FindOptions<Block, Block>
            {
                //Limit = 100,
                Sort = Builders<Block>.Sort.Ascending(o => o.Height)
            };
            var builder = Builders<Block>.Filter;
            var filterDefinition = builder.And(builder.Eq("BlockType", BlockTypes.Consolidation),
                builder.Eq("ServiceHash", belongToSvcHash));

            var result = await _blocks.FindAsync(filterDefinition, options);
            return result.ToList().Cast<ConsolidationBlock>().ToList();
        }

        //private async Task<List<TransactionBlock>> GetAccountBlockListAsync(string AccountId)
        //{
        //    var finds = await _blocks.FindAsync(x => x.AccountID == AccountId);
        //    var list = await finds.ToListAsync();
        //    var result = list.OrderBy(y => y.Index).ToList();
        //    return result;
        //}

        public async Task<Block> FindLatestBlockAsync()
        {
            var options = new FindOptions<Block, Block>
            {
                Limit = 1,
                Sort = Builders<Block>.Sort.Descending(o => o.TimeStamp)
            };

            var result = await (await _blocks.FindAsync(FilterDefinition<Block>.Empty, options)).FirstOrDefaultAsync();
            return result;
        }

        public async Task<Block> FindLatestBlockAsync(string AccountId)
        {
            var options = new FindOptions<Block, Block>
            {
                Limit = 1,
                Sort = Builders<Block>.Sort.Descending(o => o.Height)
            };
            var filter = Builders<Block>.Filter.Eq("AccountID", AccountId);

            var result = await (await _blocks.FindAsync(filter, options)).FirstOrDefaultAsync();
            return result;
        }

        public async Task<TokenGenesisBlock> FindTokenGenesisBlockAsync(string Hash, string Ticker)
        {
            //TokenGenesisBlock result = null;
            if (!string.IsNullOrEmpty(Hash))
            {
                var result = await (await _blocks.FindAsync(x => x.Hash == Hash)).FirstOrDefaultAsync();
                if (result != null)
                    return result as TokenGenesisBlock;
            }

            var builder = Builders<Block>.Filter;
            var filterDefinition = builder.And(builder.Or(builder.Eq("BlockType", BlockTypes.TokenGenesis), builder.Eq("BlockType", BlockTypes.LyraTokenGenesis)), builder.Eq("Ticker", Ticker));
            var blocks = await _blocks.FindAsync(filterDefinition);
            return await blocks.FirstOrDefaultAsync() as TokenGenesisBlock;
        }

        public async Task<List<TokenGenesisBlock>> FindTokenGenesisBlocksAsync(string keyword)
        {
            var builder = Builders<Block>.Filter;
            var filterDefinition = builder.Eq("BlockType", BlockTypes.TokenGenesis);
            var result = await _blocks.FindAsync(filterDefinition);

            if (string.IsNullOrEmpty(keyword))
            {
                return result.ToList().Cast<TokenGenesisBlock>().ToList();
            }
            else
            {
                return result.ToList().Cast<TokenGenesisBlock>().Where(a => a.Ticker.Contains(keyword)).ToList();
            }
        }

        public async Task<NullTransactionBlock> FindNullTransBlockByHashAsync(string hash)
        {
            var builder = Builders<Block>.Filter;
            var filterDefinition = builder.And(builder.Eq("BlockType", BlockTypes.NullTransaction), builder.Eq("FailedBlockHash", hash));
            var result = await _blocks.FindAsync(filterDefinition);

            return await result.FirstOrDefaultAsync() as NullTransactionBlock;
        }

        public async Task<bool> WasAccountImportedAsync(string ImportedAccountId)
        {
            var p1 = new BsonArray();
            p1.Add(BlockTypes.ImportAccount);
            p1.Add(BlockTypes.OpenAccountWithImport);

            var builder = Builders<Block>.Filter;
            var filterDefinition = builder.And(builder.In("BlockType", p1), builder.And(builder.Eq("ImportedAccountId", ImportedAccountId)));

            var result = await (await _blocks.FindAsync(filterDefinition)).FirstOrDefaultAsync();

            return result != null;
        }

        public async Task<bool> WasAccountImportedAsync(string ImportedAccountId, string AccountId)
        {
            var p1 = new BsonArray();
            p1.Add(BlockTypes.ImportAccount);
            p1.Add(BlockTypes.OpenAccountWithImport);

            var builder = Builders<Block>.Filter;
            var filterDefinition = builder.And(builder.In("BlockType", p1), builder.And(builder.Eq("ImportedAccountId", ImportedAccountId)));

            var result = await (await _blocks.FindAsync(filterDefinition)).FirstOrDefaultAsync();
            if (result == null)
                return false;

            return (result as ImportAccountBlock).AccountID == AccountId;
        }

        public async Task<Block> FindBlockByHashAsync(string hash)
        {
            if (string.IsNullOrEmpty(hash))
                return null;

            var options = new FindOptions<Block, Block>
            {
                Limit = 1,
            };
            var filter = Builders<Block>.Filter.Eq("Hash", hash);

            var block = await (await _blocks.FindAsync(filter)).FirstOrDefaultAsync();
            return block;
        }

        public async Task<Block> FindBlockByHashAsync(string AccountId, string hash)
        {
            var builder = Builders<Block>.Filter;
            var filterDefinition = builder.And(builder.Eq("Hash", hash), builder.Eq("AccountID", AccountId));

            var block = await (await _blocks.FindAsync(filterDefinition)).FirstOrDefaultAsync();
            return block as TransactionBlock;
        }

        public async Task<List<NonFungibleToken>> GetNonFungibleTokensAsync(string AccountId)
        {

            var p1 = new BsonArray();
            p1.Add(BlockTypes.ReceiveTransfer);
            p1.Add(BlockTypes.OpenAccountWithReceiveTransfer);
            //p1.Add(BlockTypes.OpenAccountWithImport);

            var builder = Builders<Block>.Filter;
            var filterDefinition = builder.And(builder.In("BlockType", p1), builder.And(builder.Eq("AccountID", AccountId), builder.Ne("NonFungibleToken", BsonNull.Value)));

            var allNonFungibleReceiveBlocks = await (await _blocks.FindAsync(filterDefinition)).ToListAsync();

            var the_list = new List<NonFungibleToken>();

            foreach (TransactionBlock receiveBlock in allNonFungibleReceiveBlocks)
            {
                the_list.Add(receiveBlock.NonFungibleToken);
            }

            if (the_list.Count > 0)
                return the_list;

            return null;
        }


        public async Task<TransactionBlock> FindBlockByPreviousBlockHashAsync(string previousBlockHash)
        {
            var builder = Builders<Block>.Filter;
            var filterDefinition = builder.Eq("PreviousHash", previousBlockHash);
            var result = await _blocks.FindAsync(filterDefinition);

            return await result.FirstOrDefaultAsync() as TransactionBlock;
        }

        /// <summary>
        /// Ignores fee blocks!
        /// </summary>
        /// <param name="hash"></param>
        /// <returns></returns>
        public async Task<ReceiveTransferBlock> FindBlockBySourceHashAsync(string hash)
        {
            var builder = Builders<Block>.Filter;
            var filterDefinition = builder.Eq("SourceHash", hash);

            var result = await (await _blocks.FindAsync(filterDefinition)).ToListAsync();

            foreach (var block in result)
            {
                if (block.BlockType == BlockTypes.OpenAccountWithReceiveFee || block.BlockType == BlockTypes.ReceiveFee)
                    continue;
                else
                    return block as ReceiveTransferBlock;
            }
            return null;
        }


        public async Task<TransactionBlock> FindBlockByIndexAsync(string AccountId, Int64 index)
        {
            var builder = new FilterDefinitionBuilder<Block>();
            var filterDefinition = builder.And(builder.Eq("AccountID", AccountId),
                builder.Eq("Height", index));

            var block = await (await _blocks.FindAsync(filterDefinition)).FirstOrDefaultAsync();
            return block as TransactionBlock;
        }

        public async Task<Block> FindServiceBlockByIndexAsync(string blockType, Int64 index)
        {
            BlockTypes types;
            if (Enum.TryParse<BlockTypes>(blockType, out types))
            {
                var builder = new FilterDefinitionBuilder<Block>();
                var filterDefinition = builder.And(builder.Eq("BlockType", types),
                    builder.Eq("Height", index));

                var block = await (await _blocks.FindAsync(filterDefinition)).FirstOrDefaultAsync();
                return block;
            }
            else
                return null;
        }

        private async Task<ReceiveTransferBlock> FindLastRecvBlock(string AccountId)
        {
            var options = new FindOptions<Block, Block>
            {
                Limit = 1,
                Sort = Builders<Block>.Sort.Descending(o => o.Height)
            };
            var builder = new FilterDefinitionBuilder<Block>();
            var filterDefinition = builder.And(builder.Eq("AccountID", AccountId),
                    builder.Eq("BlockType", BlockTypes.ReceiveTransfer));

            var result = await (await _blocks.FindAsync(filterDefinition, options)).FirstOrDefaultAsync();
            return result as ReceiveTransferBlock;
        }

        public async Task<SendTransferBlock> FindUnsettledSendBlockAsync(string AccountId)
        {
            //if (await WasAccountImportedAsync(AccountId))
            //    return null;

            // First  let's check the "main" account
            var send_block = await FindUnsettledSendBlockByDestinationAccountIdAsync(AccountId);
            if (send_block != null)
                return send_block;

            // Now let's check if there is anything sent to the imported accounts linked to this account
            var import_blocks = await GetImportedAccountBlocksAsync(AccountId);
            //if (import_blocks == null || import_blocks.Count == 0)
            //    return null;

            foreach (ImportAccountBlock importBlock in import_blocks)
            {
                send_block = await FindUnsettledSendBlockForImportedAccountAsync(importBlock.ImportedAccountId, AccountId);
                if (send_block != null)
                    return send_block;
            }

            return null;
        }

        // look up by destination account
        public async Task<SendTransferBlock> FindUnsettledSendBlockByDestinationAccountIdAsync(string AccountId)
        {

            /* send and receive blocks ar from different account chains so their indexes are not related! 
            // get last settled receive block
            long fromIndex = 0;
            var lastRecvBlock = await FindLastRecvBlock(AccountId);
            if (lastRecvBlock != null)
            {
                var lastSendToThisAccountBlock = await FindBlockByHashAsync(lastRecvBlock.SourceHash);

                if (lastSendToThisAccountBlock != null)
                    fromIndex = lastSendToThisAccountBlock.Height;
            }
            */

            // First, let find all send blocks:
            // (It can be optimzed as it's going to be growing, so it can be called with munimum Service Chain Height parameter to look only for recent blocks) 
            var builder = Builders<Block>.Filter;
            //var filterDefinition = builder.Eq("DestinationAccountId", AccountId) & builder.Gt("Height", fromIndex);
            var filterDefinition = builder.Eq("DestinationAccountId", AccountId);

            var allSendBlocks = await (await _blocks.FindAsync(filterDefinition)).ToListAsync();

            foreach (SendTransferBlock sendBlock in allSendBlocks)
            {
                //// Now, let's try to fetch the corresponding receive block:
                //var p1 = new BsonArray();
                //p1.Add((int)BlockTypes.ReceiveTransfer);
                //p1.Add((int)BlockTypes.OpenAccountWithReceiveTransfer);
                
                //var builder1 = Builders<Block>.Filter;
                //var filterDefinition1 = builder1.And(builder1.In("BlockType", p1), builder1.And(builder1.Eq("AccountID", AccountId), builder1.Eq("SourceHash", sendBlock.Hash)));

                //var result = await (await _blocks.FindAsync(filterDefinition1)).FirstOrDefaultAsync();

                var result = await FindReceiveBlockAsync(AccountId, sendBlock.Hash);

                if (result == null)
                    return sendBlock;
            }
            return null;
        }

        // look up by receive blocks that were sent to imported account
        private async Task<SendTransferBlock> FindUnsettledSendBlockForImportedAccountAsync(string ImportedAccountId, string AccountId)
        {

            // First, let find all send blocks:
            // (It can be optimzed as it's going to be growing, so it can be called with munimum Service Chain Height parameter to look only for recent blocks) 
            var builder = Builders<Block>.Filter;
            var filterDefinition = builder.Eq("DestinationAccountId", ImportedAccountId);
            var allSendBlocks = await (await _blocks.FindAsync(filterDefinition)).ToListAsync();

            foreach (SendTransferBlock sendBlock in allSendBlocks)
            {
                //// Now, let's try to fetch the corresponding receive block:
                var result = await FindReceiveBlockAsync(AccountId, sendBlock.Hash);
                if (result == null)
                {
                    // let's make sure this transfer was not received BEFORE the account was imported!
                    result = await FindReceiveBlockAsync(ImportedAccountId, sendBlock.Hash);
                    if (result == null)
                        return sendBlock;
                }
            }
            return null;
        }

        private async Task<ReceiveTransferBlock> FindReceiveBlockAsync(string AccountId, string SourceHash)
        {
            var p1 = new BsonArray();
            p1.Add((int)BlockTypes.ReceiveTransfer);
            p1.Add((int)BlockTypes.OpenAccountWithReceiveTransfer);

            var builder1 = Builders<Block>.Filter;
            var filterDefinition1 = builder1.And(builder1.In("BlockType", p1), builder1.And(builder1.Eq("AccountID", AccountId), builder1.Eq("SourceHash", SourceHash)));

            return await (await _blocks.FindAsync(filterDefinition1)).FirstOrDefaultAsync() as ReceiveTransferBlock;
        }

        // Check if the account has any imported accounts and return the list of them if they exist
        public async Task<List<Block>> GetImportedAccountBlocksAsync(string AccountId)
        {
            var p1 = new BsonArray();
            p1.Add((int)BlockTypes.ImportAccount);
            p1.Add((int)BlockTypes.OpenAccountWithImport);

            var builder = Builders<Block>.Filter;
            var filterDefinition = builder.Eq("AccountID", AccountId) & builder.In("BlockType", p1);

            var import_blocks = await(await _blocks.FindAsync(filterDefinition)).ToListAsync();
            return import_blocks;
        }

        public async Task<IEnumerable<ServiceBlock>> FindUnsettledFeeBlockAsync(string AuthorizerAccountId)
        {
            // !!! TO DO - take care of fees for imported accounts!!!!
            // get the latest feeblock
            // get all new service since the latest feeblock
            var options = new FindOptions<Block, Block>
            {
                Limit = 1,
                Sort = Builders<Block>.Sort.Descending(o => o.Height)
            };
            var builder = new FilterDefinitionBuilder<Block>();
            var filterDefinition = builder.And(builder.Eq("AccountID", AuthorizerAccountId),
                    builder.Eq("BlockType", BlockTypes.ReceiveAuthorizerFee));

            long fromHeight = 1;
            var latestFb = await(await _blocks.FindAsync(filterDefinition, options)).FirstOrDefaultAsync();
            if(latestFb != null)
            {
                fromHeight = (latestFb as ReceiveAuthorizerFeeBlock).ServiceBlockHeight;
            }

            //var nodeFilter = builder.AnyIn("Authorizers", new[] { AuthorizerAccountId });
            var nodeFilter = builder.Eq("Authorizers.AccountID", AuthorizerAccountId);
            var heightFilter = builder.Gt("Height", fromHeight);
            var feeFilter = builder.Gt("FeesGenerated", 21);    // make sure that every node has a minimal share

            var options2 = new FindOptions<Block, Block>
            {
                Limit = 100,
                Sort = Builders<Block>.Sort.Ascending(o => o.Height)
            };

            var sbs = await _blocks.FindAsync(builder.And(nodeFilter, heightFilter, feeFilter), options2);
            //if (sbs.Any())
            return sbs.ToList().Cast<ServiceBlock>();
            //else
            //    return Enumerable.Empty<ServiceBlock>();
        }

        /// <summary>
        /// Returns the first unexecuted and incancelled trade aimed to an order created on the account.
        /// </summary>
        /// <param name="AccountId"></param>
        /// <param name="BuyTokenCode">
        /// The code of the token being purchased (optional).
        /// </param>
        /// <param name="SellTokenCode">
        /// The code of the token being sold (optional).
        /// </param>
        /// <returns></returns>
        public TradeBlock FindUnexecutedTrade(string AccountId, string BuyTokenCode, string SellTokenCode)
        {
            if (BuyTokenCode == "*")
                BuyTokenCode = null;

            if (SellTokenCode == "*")
                SellTokenCode = null;

            var trades_builder = Builders<Block>.Filter;
            var trades_filterDefinition = trades_builder.And(trades_builder.Eq("BlockType", BlockTypes.Trade), trades_builder.Eq("DestinationAccountId", AccountId));

            var trades = _blocks.Find(trades_filterDefinition).ToList();

            foreach (TradeBlock trade in trades)
            {
                var exec_builder = Builders<Block>.Filter;
                var exec_filterDefinition = exec_builder.And(exec_builder.Eq("BlockType", BlockTypes.ExecuteTradeOrder), exec_builder.Eq("TradeId", trade.Hash));
                var trade_execution = _blocks.Find(exec_filterDefinition);

                if (trade_execution.Any())
                    continue;

                var cancel_builder = Builders<Block>.Filter;
                var cancel_filterDefinition = cancel_builder.And(cancel_builder.Eq("BlockType", BlockTypes.CancelTradeOrder), cancel_builder.Eq("TradeOrderId", trade.TradeOrderId));
                var trade_cancellation = _blocks.Find(cancel_filterDefinition);

                if (trade_cancellation.Any())
                    continue;

                if (!string.IsNullOrEmpty(BuyTokenCode) && BuyTokenCode != trade.BuyTokenCode)
                    continue;

                if (!string.IsNullOrEmpty(SellTokenCode) && SellTokenCode != trade.SellTokenCode)
                    continue;

                return trade;
            }
            return null;
        }

        public List<TradeOrderBlock> GetTradeOrderBlocks()
        {
            var list = new List<TradeOrderBlock>();

            //var blocks = _blocks.Find(Query.EQ("BlockType", BlockTypes.TradeOrder.ToString()));

            var builder = Builders<Block>.Filter;
            var filterDefinition = builder.Eq("BlockType", BlockTypes.TradeOrder);
            var trade_blocks = _blocks.Find(filterDefinition).ToList();

            foreach (TradeOrderBlock block in trade_blocks)
                list.Add(block);

            return list;
        }

        public async Task<List<TradeOrderBlock>> GetSellTradeOrders(string SellTokenCode, string BuyTokenCode)
        {
            var list = new List<TradeOrderBlock>();

            var options = new FindOptions<Block, Block>
            {
                Limit = 1000,
                Sort = Builders<Block>.Sort.Descending(o => o.TimeStamp)
            };

            var builder = Builders<Block>.Filter;

            var filterDefinition = builder.And(builder.Eq("BlockType", BlockTypes.TradeOrder), builder.Eq("OrderType", TradeOrderTypes.Sell), builder.Eq("SellTokenCode", SellTokenCode), builder.Eq("BuyTokenCode", BuyTokenCode));

            var trade_blocks = await _blocks.Find(filterDefinition).ToListAsync();

            foreach (TradeOrderBlock block in trade_blocks)
                list.Add(block);

            return list;
        }

        public async Task<List<TradeOrderBlock>> GetSellTradeOrdersForToken(string BuyTokenCode)
        {
            var list = new List<TradeOrderBlock>();

            var options = new FindOptions<Block, Block>
            {
                Limit = 1000,
                Sort = Builders<Block>.Sort.Descending(o => o.TimeStamp) 
            };

            var builder = Builders<Block>.Filter;

            var filterDefinition = builder.And(builder.Eq("BlockType", BlockTypes.TradeOrder), builder.Eq("OrderType", TradeOrderTypes.Sell), builder.Eq("BuyTokenCode", BuyTokenCode));

            var trade_blocks = await _blocks.Find(filterDefinition).ToListAsync();

            foreach (TradeOrderBlock block in trade_blocks)
                list.Add(block);

            return list;
        }

        // returns the list of hashes (order IDs) of all cancelled trade order blocks
        public List<string> GetTradeOrderCancellations()
        {
            var list = new List<string>();
            //var blocks = _blocks.Find(Query.EQ("BlockType", BlockTypes.CancelTradeOrder.ToString()));

            var builder = Builders<Block>.Filter;
            var filterDefinition = builder.Eq("BlockType", BlockTypes.CancelTradeOrder);
            var blocks = _blocks.Find(filterDefinition).ToList();

            foreach (CancelTradeOrderBlock block in blocks)
                list.Add(block.TradeOrderId);

            return list;
        }

        public async Task<CancelTradeOrderBlock> GetCancelTradeOrderBlock(string TradeOrderId)
        {
            var builder = Builders<Block>.Filter;
            var filterDefinition = builder.And(builder.Eq("BlockType", BlockTypes.CancelTradeOrder), builder.Eq("TradeOrderId", TradeOrderId));
            var block = await _blocks.Find(filterDefinition).FirstOrDefaultAsync();
            return block as CancelTradeOrderBlock;
        }

        // returns the list of hashes (order IDs) of all cancelled trade order blocks
        public List<string> GetExecutedTradeOrderBlocks()
        {
            var list = new List<string>();
            //var blocks = _blocks.Find(Query.EQ("BlockType", BlockTypes.ExecuteTradeOrder.ToString()));
            var builder = Builders<Block>.Filter;
            var filterDefinition = builder.Eq("BlockType", BlockTypes.ExecuteTradeOrder);
            var blocks = _blocks.Find(filterDefinition).ToList();

            foreach (ExecuteTradeOrderBlock block in blocks)
                list.Add(block.TradeOrderId);

            return list;
        }

        public async Task<ExecuteTradeOrderBlock> GetExecuteTradeOrderBlock(string TradeOrderId)
        {
            var builder = Builders<Block>.Filter;
            var filterDefinition = builder.And(builder.Eq("BlockType", BlockTypes.ExecuteTradeOrder), builder.Eq("TradeOrderId", TradeOrderId));
            var block = await _blocks.Find(filterDefinition).FirstOrDefaultAsync();
            return block as ExecuteTradeOrderBlock;
        }

        public async Task<bool> AddBlockAsync(Block block)
        {
            if (await FindBlockByHashAsync(block.Hash) != null)
            {
                //_log.LogWarning("AccountCollection=>AddBlock: Block with such Hash already exists!");
                return false;
            }

            if(block is TransactionBlock)
            {
                var block1 = block as TransactionBlock;
                if (await FindBlockByIndexAsync(block1.AccountID, block1.Height) != null)
                {
                    _log.LogWarning("AccountCollection=>AddBlock: Block with such Index already exists!");
                    return false;
                }
            }

            //_log.LogInformation($"AddBlockAsync InsertOneAsync: {block.Height}");
            await _blocks.InsertOneAsync(block);
            return true;
        }

        public async Task RemoveBlockAsync(string hash)
        {
            var ret = await _blocks.DeleteOneAsync(a => a.Hash == hash);
            if (ret.IsAcknowledged && ret.DeletedCount == 1)
            {
               // _log.LogWarning($"RemoveBlockAsync Block {hash} removed.");
            }
            else
                _log.LogWarning($"RemoveBlockAsync Block {hash} failed.");
        }

        public void Dispose()
        {
            // nothing to dispose
        }

        //public async Task<bool> ConsolidateBlock(string hash)
        //{
        //    var options = new FindOptions<Block, Block>
        //    {
        //        Limit = 1,
        //    };
        //    var filter = Builders<Block>.Filter.Eq("Hash", hash);

        //    var updateDef = Builders<Block>.Update.Set(o => o.Consolidated, true);
        //    var result = await _blocks.UpdateOneAsync(filter, updateDef);
        //    return result.ModifiedCount == 1;
        //}

        public async Task<IEnumerable<Block>> GetAllUnConsolidatedBlocksAsync()
        {
            var options = new FindOptions<Block, Block>
            {
                Limit = 1000,
                Sort = Builders<Block>.Sort.Ascending(o => o.TimeStamp),
                Projection = Builders<Block>.Projection.Include(a => a.Hash)
            };
            var filter = Builders<Block>.Filter.Eq("Consolidated", false);
            var result = await _blocks.FindAsync(filter, options);
            return await result.ToListAsync();
        }

        //public async Task<IEnumerable<string>> GetAllUnConsolidatedBlockHashesAsync()
        //{
        //    var options = new FindOptions<Block, BsonDocument>
        //    {
        //        Limit = 1000,
        //        Sort = Builders<Block>.Sort.Ascending(o => o.TimeStamp),
        //        Projection = Builders<Block>.Projection.Include(a => a.Hash)
        //    };
        //    var filter = Builders<Block>.Filter.Eq("Consolidated", false);
        //    var result = await _blocks.FindAsync(filter, options);
        //    return (await result.ToListAsync()).Select(a => a["Hash"].AsString);
        //}

        async Task<ServiceBlock> IAccountCollectionAsync.GetServiceGenesisBlock()
        {
            var options = new FindOptions<Block, Block>
            {
                Limit = 1,
                Sort = Builders<Block>.Sort.Ascending(o => o.Height)
            };
            var filter = Builders<Block>.Filter;
            var filterDefination = filter.Eq("BlockType", BlockTypes.Service);

            var finds = await _blocks.FindAsync(filterDefination, options);
            return await finds.FirstOrDefaultAsync() as ServiceBlock;
        }

        async Task<LyraTokenGenesisBlock> IAccountCollectionAsync.GetLyraTokenGenesisBlock()
        {
            var options = new FindOptions<Block, Block>
            {
                Limit = 1
                //Sort = Builders<Block>.Sort.Ascending(o => o.Height)
            };
            var filter = Builders<Block>.Filter;
            var filterDefination = filter.Eq("BlockType", BlockTypes.LyraTokenGenesis);

            var finds = await _blocks.FindAsync(filterDefination, options);
            return await finds.FirstOrDefaultAsync() as LyraTokenGenesisBlock;
        }

        // >= startTime < endTime
        public async Task<List<Block>> GetBlocksByTimeRange(DateTime startTime, DateTime endTime)
        {
            var options = new FindOptions<Block, Block>
            {
                Sort = Builders<Block>.Sort.Ascending(o => o.TimeStamp)
            };
            var builder = Builders<Block>.Filter;
            var filter = builder.And(builder.Gte("TimeStamp", startTime), builder.Lt("TimeStamp", endTime));
            var result = await _blocks.FindAsync(filter, options);
            return await result.ToListAsync();
        }

        public async Task<IEnumerable<string>> GetBlockHashesByTimeRange(DateTime startTime, DateTime endTime)
        {
            var options = new FindOptions<Block, BsonDocument>
            {
                Sort = Builders<Block>.Sort.Ascending(o => o.TimeStamp),
                Projection = Builders<Block>.Projection.Include(a => a.Hash)
            };
            var builder = Builders<Block>.Filter;
            var filter = builder.And(builder.Gte("TimeStamp.Ticks", startTime.Ticks), builder.Lt("TimeStamp.Ticks", endTime.Ticks));
            var result = await _blocks.FindAsync(filter, options);
            return (await result.ToListAsync()).Select(a => a["Hash"].AsString);
        }

        private class VoteInfo
        {
            public string AccountID { get; set; }
            public Dictionary<string, long> Balances { get; set; }
            public long Height { get; set; }
            public string VoteFor { get; set; }
        }

        public List<Voter> GetVoters(List<string> posAccountIds, DateTime endTime)
        {
            var builder = Builders<TransactionBlock>.Filter;
            var projection = Builders<TransactionBlock>.Projection;

            var txFilter = builder.And(builder.Lt("TimeStamp", endTime));

            var atrVotes = _blocks.OfType<TransactionBlock>()
                .Aggregate()
                .Match(txFilter)
                .Project(projection.Include(a => a.Balances)
                    .Include(a => a.AccountID)
                    .Include(a => a.VoteFor)
                    .Include(a => a.Height)
                    .Exclude("_id"))
                .ToList();

            var perAtrVotes = atrVotes
                .Select(a => BsonSerializer.Deserialize<VoteInfo>(a))
                .OrderByDescending(a => a.Height)
                .GroupBy(a => a.AccountID)      // this time select the latest block of account
                .Select(g => new Voter
                {
                    AccountId = g.Key,
                    Balance = g.First().Balances.ContainsKey(LyraGlobal.OFFICIALTICKERCODE) ? g.First().Balances[LyraGlobal.OFFICIALTICKERCODE] : 0,
                    VoteFor = g.First().VoteFor
                });
                
            var votersForSb = perAtrVotes
                .Where(a => !string.IsNullOrEmpty(a.VoteFor) && posAccountIds.Contains(a.VoteFor))
                .ToList();

            return votersForSb;
        }

        public List<Vote> FindVotes(List<string> posAccountIds, DateTime endTime)
        {
            var voters = GetVoters(posAccountIds, endTime);

            var votes = voters
                .GroupBy(a => a.VoteFor)        // this time aggregate the total votes
                .Select(g => new Vote { AccountId = g.Key, Amount = g.Sum(a => a.Balance) / LyraGlobal.TOKENSTORAGERITO })
                .ToList();

            return votes;
        }

        public FeeStats GetFeeStats()
        {
            var sbs = _blocks.OfType<ServiceBlock>()
                    .Aggregate()
                    .SortBy(x => x.Height)
                    .ToList();

            decimal totalFeeConfirmed = sbs.Sum(a => a.FeesGenerated) / LyraGlobal.TOKENSTORAGERITO;

            var builder = Builders<TransactionBlock>.Filter;
            var projection = Builders<TransactionBlock>.Projection;

            var txFilter = builder.And(builder.Gt("TimeStamp", sbs.Last().TimeStamp));

            var unTxs = _blocks.OfType<TransactionBlock>()
                .Aggregate()
                .Match(txFilter)
                .ToList();

            decimal totalFeeUnConfirmed = unTxs.Sum(a => a.Fee);

            // confirmed earns
            IEnumerable<RevnuItem> GetRevnuFromSb(decimal fees, ServiceBlock sb)
            {
                return sb.Authorizers.Keys.Select(a => new RevnuItem { accId = a, revenue = Math.Round(fees / sb.Authorizers.Count, 8) });
            };
            IEnumerable<RevnuItem> Merge(IEnumerable<RevnuItem> List1, IEnumerable<RevnuItem> List2)
            {
                var list3 = List1.Concat(List2)
                             .GroupBy(x => x.accId)
                             .Select( g =>
                                 new RevnuItem
                                 {
                                     accId = g.Key,
                                     revenue = Math.Round(g.Sum(x => x.revenue), 8)
                                 });
                return list3;
            };
            var confimed = Enumerable.Empty<RevnuItem>();
            for(int i = sbs.Count - 1; i > 0; i--)
            {
                confimed = Merge(confimed, GetRevnuFromSb(sbs[i].FeesGenerated / LyraGlobal.TOKENSTORAGERITO, sbs[i - 1]));
            }

            // unconfirmed
            var unconfirm = sbs.Last().Authorizers.Keys.Select(a => new RevnuItem { accId = a, revenue = Math.Round(totalFeeUnConfirmed / sbs.Last().Authorizers.Count, 8) });

            return new FeeStats { TotalFeeConfirmed = totalFeeConfirmed,
                TotalFeeUnConfirmed = totalFeeUnConfirmed,
                ConfirmedEarns = confimed.OrderByDescending(a => a.revenue).ToList(),
                UnConfirmedEarns = unconfirm.OrderByDescending(a => a.revenue).ToList()
            };
        }
    }

    public class FeeStats
    {
        public decimal TotalFeeConfirmed { get; set; }
        public decimal TotalFeeUnConfirmed { get; set; }

        public List<RevnuItem> ConfirmedEarns { get; set; }
        public List<RevnuItem> UnConfirmedEarns { get; set; }
    }

    public class RevnuItem
    {
        public string accId { get; set; }
        public decimal revenue { get; set; }
    }
}