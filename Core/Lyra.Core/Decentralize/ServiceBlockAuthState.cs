﻿using Lyra.Core.API;
using Neo;
using System;
using System.Collections.Generic;
using System.Text;

namespace Lyra.Core.Decentralize
{
    public class ServiceBlockAuthState : AuthState
    {
        private List<string> _allVoters;
        public ServiceBlockAuthState(List<string> AllVoters, bool haveWaiter = false) : base(haveWaiter)
        {
            _allVoters = AllVoters;
        }

        public override int WinNumber
        {
            get
            {
                var minCount = LyraGlobal.GetMajority(_allVoters == null ? base.WinNumber : _allVoters.Count);
                if (minCount < ProtocolSettings.Default.StandbyValidators.Length)
                    return ProtocolSettings.Default.StandbyValidators.Length;
                else
                    return minCount;
            }            
         }

        public override bool CheckSenderValid(string from)
        {
            return _allVoters == null ? base.CheckSenderValid(from) : _allVoters.Contains(from);
        }
    }
}
