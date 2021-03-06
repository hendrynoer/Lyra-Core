﻿using Lyra.Core.API;
using Lyra.Core.Blocks;
using LyraWallet.Services;
using LyraWallet.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Essentials;
using Lyra.Exchange;
using Lyra.Core.Accounts;
using Microsoft.Extensions.Hosting;
using Lyra.Core.Cryptography;

namespace LyraWallet.Models
{
    public delegate void NodeNotifyMessage(string action, string catalog, string extInfo);
    public class WalletContainer : BaseViewModel
    {
        private string dataStoragePath;
        private string walletFn;

        private string currentNetwork;
        private string accountID;
        private string privateKey;
        private string voteFor;

        private Lyra.Core.Accounts.Wallet wallet;
        private Dictionary<string, Decimal> balances;
        private List<string> tokenList;

        // working status
        private string busyMessage;

        private LyraRestClient _nodeApiClient;
        private LyraRestNotify _notifyApiClient;

        public string DataStoragePath { get => dataStoragePath; set => SetProperty(ref dataStoragePath, value); }
        public string WalletFn { get => walletFn; set => SetProperty(ref walletFn, value); }
        public string CurrentNetwork { get => currentNetwork; set => SetProperty(ref currentNetwork, value); }
        public string AccountID { get => accountID; set => SetProperty(ref accountID, value); }
        public string PrivateKey { get => privateKey; set => SetProperty(ref privateKey, value); }
        public string VoteFor { get
            {
                return voteFor;
            }
            set
            {
                SetProperty(ref voteFor, value);
                wallet.VoteFor = value;
            }
        }

        public Dictionary<string, decimal> Balances { get => balances; set {
                var sorted = value?.OrderBy(a => a.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                SetProperty(ref balances, sorted); }
        }
        public List<string> TokenList { get => tokenList; set => SetProperty(ref tokenList, value); }
        public string BusyMessage { get => busyMessage; set => SetProperty(ref busyMessage, value); }

        private CancellationTokenSource _cancel;
        public event NodeNotifyMessage OnBalanceChanged;
        public event NodeNotifyMessage OnExchangeOrderChanged;
        public async Task OpenWalletFileAsync()
        {
            if (wallet != null)
                throw new Exception("Wallet opening");

            // setup API clients
            var platform = DeviceInfo.Platform.ToString();
            _nodeApiClient = LyraRestClient.Create(CurrentNetwork, platform, AppInfo.Name, AppInfo.VersionString);

            var securedStore = new SecuredWalletStore(App.Container.DataStoragePath);
            wallet = Lyra.Core.Accounts.Wallet.Open(securedStore, "My Account", "");

            AccountID = wallet.AccountId;
            PrivateKey = wallet.PrivateKey;
            VoteFor = wallet.VoteFor;

            if (AccountID == null || PrivateKey == null)
                throw new Exception("no private key");

            //var client = App.ServiceProvider.GetService(typeof(IHostedService));

            //_nodeApiClient = new DAGAPIClient((DAGClientHostedService)client);
            //while ((client as DAGClientHostedService).Node == null)
            //    await Task.Delay(100);
            await wallet.Sync(_nodeApiClient);
            //_notifyApiClient = new LyraRestNotify(platform, LyraGlobal.SelectNode(CurrentNetwork).restUrl + "LyraNotify/");

            //_cancel = new CancellationTokenSource();
            //await _notifyApiClient.BeginReceiveNotifyAsync(AccountID, wallet.SignAPICall(), (source, action, catalog, extInfo) => {
            //    Device.BeginInvokeOnMainThread(() =>
            //    {
            //        switch (source)
            //        {
            //            case NotifySource.Balance:
            //                OnBalanceChanged?.Invoke(action, catalog, extInfo);
            //                break;
            //            case NotifySource.Dex:
            //                OnExchangeOrderChanged?.Invoke(action, catalog, extInfo);
            //                break;
            //            default:
            //                break;
            //        }
            //    });
            //}, _cancel.Token);
        }

        public void CreateNew(string network_id)
        {
            if (wallet != null)
                throw new Exception("Wallet opening");

            var path = DependencyService.Get<IPlatformSvc>().GetStoragePath();
            File.WriteAllText(path + "/network.txt", network_id);

            var secureStore = new SecuredWalletStore(path);
            (var privateKey, var publicKey) = Signatures.GenerateWallet();

            wallet = Wallet.Create(secureStore, "My Account", "", network_id, privateKey);
        }

        public void CreateByPrivateKey(string network_id, string privatekey)
        {
            if (wallet != null)
                throw new Exception("Wallet opening");

            if (!Signatures.ValidatePrivateKey(privatekey))
            {
                wallet = null;
                throw new InvalidDataException("Invalid Private Key");
            }

            var path = DependencyService.Get<IPlatformSvc>().GetStoragePath();
            File.WriteAllText(path + "/network.txt", network_id);
            var secureStore = new SecuredWalletStore(path);
            wallet = Wallet.Create(secureStore, "My Account", "", network_id, privatekey);
        }

        public void GetBalance()
        {
            var latestBlock = wallet.GetLatestBlock();
            App.Container.Balances = latestBlock?.Balances.ToDictionary(p => p.Key, p => p.Value.ToBalanceDecimal());
            App.Container.TokenList = App.Container.Balances?.Keys.ToList();
        }
        public async Task RefreshBalance(string webApiUrl = null)
        {
            //APIResultCodes result = APIResultCodes.UndefinedError;
            //int retryCount = 0;
            //while(retryCount < 5)
            //{
            //    try
            //    {
            //        result = await wallet.Sync(_nodeApiClient);
            //        break;
            //    }
            //    catch (Exception ex)
            //    {
            //        retryCount++;
            //    }
            //}

            //if (result == APIResultCodes.Success)
            //{
            //    var lastBlock = wallet.GetLatestBlock();
            //    App.Container.Balances = lastBlock?.Balances.ToDictionary(p => p.Key, p => p.Value.ToBalanceDecimal());
            //    App.Container.TokenList = App.Container.Balances?.Keys.ToList();
            //}
            //else
            //{
            //    throw new Exception(result.ToString());
            //}
        }

        public async Task Transfer(string tokenName, string targetAccount, decimal amount, bool ToExchange = false)
        {
            // refresh balance before send. other wise Null Ex
            await RefreshBalance();
            if(App.Container.Balances[tokenName] < amount)
            {
                throw new Exception("Not enough funds for " + tokenName);
            }

            var result = await wallet.Send(amount, targetAccount, tokenName, ToExchange);
            if (result.ResultCode != APIResultCodes.Success)
            {
                throw new Exception(result.ToString());
            }
        }

        public async Task CreateToken(string tokenName, string tokenDomain,
            string description, decimal totalSupply, int precision, string ownerName, string ownerAddress)
        {
            var result = await wallet.CreateToken(tokenName, tokenDomain ?? "", description ?? "", Convert.ToSByte(precision), totalSupply,
                true, ownerName ?? "", ownerAddress ?? "", null, ContractTypes.Default, null);
            if (result.ResultCode != APIResultCodes.Success)
            {
                throw new Exception(result.ToString());
            }
        }

        public async Task<List<BlockInfo>> GetBlocks()
        {
            var blocks = new List<BlockInfo>();
            var height = wallet.GetLocalAccountHeight();
            for (var i = height; i > 0; i--)
            {
                var block = await wallet.GetBlockByIndex(i);
                blocks.Add(new BlockInfo()
                {
                    index = block.Height,
                    timeStamp = block.TimeStamp,
                    hash = block.Hash,
                    type = block.BlockType.ToString(),
                    balance = block.Balances.Aggregate(new StringBuilder(),
                          (sb, kvp) => sb.AppendFormat("{0}{1} = {2}",
                                       sb.Length > 0 ? ", " : "", kvp.Key, kvp.Value.ToBalanceDecimal()),
                          sb => sb.ToString())
                });
            }
            return blocks;
        }

        public async Task<List<string>> GetTokens(string keyword)
        {
            var result = await wallet.GetTokenNames(keyword);
            return result;
        }

        public async Task CloseWallet()
        {
            await Task.Run(() => {
                if(_cancel != null)
                    _cancel.Cancel();
                wallet = null;
            });
        }

        public async Task Remove()
        {
            if (wallet != null)
            {
                await CloseWallet();
            }

            wallet = null;
            Balances = null;

            if (File.Exists(App.Container.WalletFn))
                File.Delete(App.Container.WalletFn);
        }

        public async Task<string> GetExchangeAccountId()
        {
            var result = await _nodeApiClient.CreateExchangeAccount(AccountID, wallet.SignAPICallAsync());
            if (result.ResultCode == APIResultCodes.Success)
                return result.AccountId;
            else
                return null;
        }

        public async Task<CancelKey> SubmitExchangeOrderAsync(TokenTradeOrder order)
        {
            return await _nodeApiClient.SubmitExchangeOrder(order);
        }

        public async Task<APIResult> CancelExchangeOrder(string key)
        {
            return await _nodeApiClient.CancelExchangeOrder(AccountID, wallet.SignAPICallAsync(), key);
        }

        public async Task<APIResult> RequestMarket(string tokenName)
        {
            return await _nodeApiClient.RequestMarket(tokenName);
        }

        public async Task<List<ExchangeOrder>> GetOrdersForAccount(string AccountId)
        {
            return await _nodeApiClient.GetOrdersForAccount(AccountId, wallet.SignAPICallAsync());
        }

        public async Task<Dictionary<string, decimal>> GetExchangeBalance()
        {
            var result = await _nodeApiClient.GetExchangeBalance(AccountID, wallet.SignAPICallAsync());
            return result.Balance;
        }
    }
}
