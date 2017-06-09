﻿/*
    Copyright (C) 2014 Omega software d.o.o.

    This file is part of Rhetos.

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as
    published by the Free Software Foundation, either version 3 of the
    License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.ServiceModel.Dispatcher;
using Rhetos.Logging;
using System.ServiceModel.Description;
using System.ServiceModel.Channels;

namespace Rhetos.Web
{
    public class GlobalErrorHandler : IErrorHandler
    {
        private readonly ILogger Logger;

        public GlobalErrorHandler(ILogProvider logProvider)
        {
            this.Logger = logProvider.GetLogger("GlobalErrorHandler");
        }

        public bool HandleError(Exception error)
        {
            if (error is UserException)
                Logger.Trace(error.ToString);
            else if (error is LegacyClientException)
            {
                if (((LegacyClientException)error).Severe)
                    Logger.Info(error.ToString);
                else
                    Logger.Trace(error.ToString);
            }
            else if (error is ClientException)
                Logger.Info(error.ToString);
            else
                Logger.Error(error.ToString);
            return false;
        }

        public void ProvideFault(
            Exception error, 
            MessageVersion version, 
            ref Message fault)
        {
        }
    }
}