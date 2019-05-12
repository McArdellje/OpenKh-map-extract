﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace kh.kh2.Messages.Internals
{
    internal class InternationalSystemEncode : IMessageEncode
    {
        private static readonly Dictionary<MessageCommand, KeyValuePair<byte, BaseCmdModel>> _tableCommands =
            InternationalSystemDecode._table
            .Where(x => x.Value != null && x.Value.Command != MessageCommand.PrintText)
            .GroupBy(x => x.Value.Command)
            .ToDictionary(x => x.Key, x => x.First());

        private static readonly Dictionary<char, byte> _tableCharacters =
            InternationalSystemDecode._table
            .Where(x => x.Value?.Command == MessageCommand.PrintText)
            .ToDictionary(x => x.Value.Text[0], x => x.Key);

        private void AppendEncodedMessageCommand(List<byte> list, MessageCommandModel messageCommand)
        {
            if (messageCommand.Command == MessageCommand.PrintText)
                AppendEncodedText(list, messageCommand.Text);
            else
                AppendEncodedCommand(list, messageCommand.Command, messageCommand.Data);
        }
        
        private void AppendEncodedCommand(List<byte> list, MessageCommand command, byte[] data)
        {
            if (!_tableCommands.TryGetValue(command, out var pair))
                throw new ArgumentException($"The command {command} it is not supported by the specified encoding.");

            list.Add(pair.Key);
            for (var i = 0; i < pair.Value.Length; i++)
                list.Add(data[i]);
        }

        private void AppendEncodedText(List<byte> list, string text)
        {
            foreach (var ch in text)
                AppendEncodedChar(list, ch);
        }

        private void AppendEncodedChar(List<byte> list, char ch)
        {
            if (!_tableCharacters.TryGetValue(ch, out var data))
                throw new ArgumentException($"The character {ch} it is not supported by the specified encoding.");
            
            list.Add(data);
        }

        public byte[] Encode(List<MessageCommandModel> messageCommands)
        {
            var list = new List<byte>(100);
            foreach (var model in messageCommands)
                AppendEncodedMessageCommand(list, model);

            return list.ToArray();
        }
    }
}
