/******************************************************************************
* The MIT License
* Copyright (c) 2003 Novell Inc.  www.novell.com
*
* Permission is hereby granted, free of charge, to any person obtaining  a copy
* of this software and associated documentation files (the Software), to deal
* in the Software without restriction, including  without limitation the rights
* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
* copies of the Software, and to  permit persons to whom the Software is
* furnished to do so, subject to the following conditions:
*
* The above copyright notice and this permission notice shall be included in
* all copies or substantial portions of the Software.
*
* THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NON INFRINGEMENT. IN NO EVENT SHALL THE
* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
* SOFTWARE.
*******************************************************************************/

//
// Novell.Directory.Ldap.LdapConnection.cs
//
// Author:
//   Sunil Kumar (Sunilk@novell.com)
//
// (C) 2003 Novell, Inc (http://www.novell.com)
//

using System;
using System.Collections;
using System.Collections.Concurrent;
using Novell.Directory.Ldap.Logging;
using Novell.Directory.Ldap.Rfc2251;
using Novell.Directory.Ldap.Sasl;
using Novell.Directory.Ldap.Utilclass;

namespace Novell.Directory.Ldap
{
    /// <summary>
    ///     The central class that encapsulates the connection
    ///     to a directory server through the Ldap protocol.
    ///     LdapConnection objects are used to perform common Ldap
    ///     operations such as search, modify and add.
    ///     In addition, LdapConnection objects allow you to bind to an
    ///     Ldap server, set connection and search constraints, and perform
    ///     several other tasks.
    ///     An LdapConnection object is not connected on
    ///     construction and can only be connected to one server at one
    ///     port. Multiple threads may share this single connection, typically
    ///     by cloning the connection object, one for each thread. An
    ///     application may have more than one LdapConnection object, connected
    ///     to the same or different directory servers.
    /// </summary>
    public partial class LdapConnection : ILdapConnection
    {
        /// <summary>
        ///     Used with search to specify that the scope of entries to search is to
        ///     search only the base object.
        ///     SCOPE_BASE = 0.
        /// </summary>
        public const int ScopeBase = 0;

        /// <summary>
        ///     Used with search to specify that the scope of entries to search is to
        ///     search only the immediate subordinates of the base object.
        ///     SCOPE_ONE = 1.
        /// </summary>
        public const int ScopeOne = 1;

        /// <summary>
        ///     Used with search to specify that the scope of entries to search is to
        ///     search the base object and all entries within its subtree.
        ///     SCOPE_SUB = 2.
        /// </summary>
        public const int ScopeSub = 2;

        /// <summary>
        ///     Used with search instead of an attribute list to indicate that no
        ///     attributes are to be returned.
        ///     NO_ATTRS = "1.1".
        /// </summary>
        public const string NoAttrs = "1.1";

        /// <summary>
        ///     Used with search instead of an attribute list to indicate that all
        ///     attributes are to be returned.
        ///     ALL_USER_ATTRS = "*".
        /// </summary>
        public const string AllUserAttrs = "*";

        /// <summary>
        ///     Specifies the Ldapv3 protocol version when performing a bind operation.
        ///     Specifies Ldap version V3 of the protocol, and is specified
        ///     when performing bind operations.
        ///     You can use this identifier in the version parameter
        ///     of the bind method to specify an Ldapv3 bind.
        ///     Ldap_V3 is the default protocol version
        ///     Ldap_V3 = 3.
        /// </summary>
        public const int LdapV3 = 3;

        /// <summary>
        ///     The default port number for Ldap servers.
        ///     You can use this identifier to specify the port when establishing
        ///     a clear text connection to a server.  This the default port.
        ///     DEFAULT_PORT = 389.
        /// </summary>
        public const int DefaultPort = 389;

        /// <summary>
        ///     The default SSL port number for Ldap servers.
        ///     DEFAULT_SSL_PORT = 636
        ///     You can use this identifier to specify the port when establishing
        ///     a an SSL connection to a server..
        /// </summary>
        public const int DefaultSslPort = 636;

        /// <summary>
        ///     A string that can be passed in to the getProperty method.
        ///     Ldap_PROPERTY_SDK = "version.sdk"
        ///     You can use this string to request the version of the SDK.
        /// </summary>
        public const string LdapPropertySdk = "version.sdk";

        /// <summary>
        ///     A string that can be passed in to the getProperty method.
        ///     Ldap_PROPERTY_PROTOCOL = "version.protocol"
        ///     You can use this string to request the version of the
        ///     Ldap protocol.
        /// </summary>
        public const string LdapPropertyProtocol = "version.protocol";

        /// <summary>
        ///     A string that can be passed in to the getProperty method.
        ///     Ldap_PROPERTY_SECURITY = "version.security"
        ///     You can use this string to request the type of security
        ///     being used.
        /// </summary>
        public const string LdapPropertySecurity = "version.security";

        /// <summary>
        ///     A string that corresponds to the server shutdown notification OID.
        ///     This notification may be used by the server to advise the client that
        ///     the server is about to close the connection due to an error
        ///     condition.
        ///     SERVER_SHUTDOWN_OID = "1.3.6.1.4.1.1466.20036".
        /// </summary>
        public const string ServerShutdownOid = "1.3.6.1.4.1.1466.20036";

        /// <summary> The OID string that identifies a StartTLS request and response.</summary>
        private const string StartTlsOid = "1.3.6.1.4.1.1466.20037";
        
        private static readonly ILog Logger = LogProvider.For<LdapConnection>();

        public virtual DebugId DebugId { get; } = DebugId.ForType<LdapConnection>();

        private LdapSearchConstraints _defSearchCons;
        private LdapControl[] _responseCtls;

        // Synchronization Object used to synchronize access to responseCtls
        private readonly object _responseCtlSemaphore = new object();

        /*
        * Constructors
        */

        /// <summary>
        ///     Constructs a new LdapConnection object, which will use the supplied
        ///     class factory to construct a socket connection during
        ///     LdapConnection.connect method.
        /// </summary>
        /// <param name="factory">
        ///     An object capable of producing a Socket.
        /// </param>
        public LdapConnection()
        {
            _defSearchCons = new LdapSearchConstraints();
            _saslClientFactories = new ConcurrentDictionary<string, ISaslClientFactory>(StringComparer.OrdinalIgnoreCase);
            Connection = new Connection();
        }

        /// <summary>
        ///     Returns the protocol version uses to authenticate.
        ///     0 is returned if no authentication has been performed.
        /// </summary>
        /// <returns>
        ///     The protol version used for authentication or 0
        ///     not authenticated.
        /// </returns>
        public int ProtocolVersion
        {
            get
            {
                var prop = Connection.BindProperties;
                if (prop == null)
                {
                    return LdapV3;
                }

                return prop.ProtocolVersion;
            }
        }

        /// <summary>
        ///     Returns the distinguished name (DN) used for as the bind name during
        ///     the last successful bind operation.  <code>null</code> is returned
        ///     if no authentication has been performed or if the bind resulted in
        ///     an aonymous connection.
        /// </summary>
        /// <returns>
        ///     The distinguished name if authenticated; otherwise, null.
        /// </returns>
        public string AuthenticationDn
        {
            get
            {
                var prop = Connection.BindProperties;
                if (prop == null)
                {
                    return null;
                }

                if (prop.Anonymous)
                {
                    return null;
                }

                return prop.AuthenticationDn;
            }
        }

        /// <summary>
        ///     Returns the method used to authenticate the connection. The return
        ///     value is one of the following:.
        ///     <ul>
        ///         <li>"none" indicates the connection is not authenticated.</li>
        ///         <li>
        ///             "simple" indicates simple authentication was used or that a null
        ///             or empty authentication DN was specified.
        ///         </li>
        ///         <li>"sasl" indicates that a SASL mechanism was used to authenticate</li>
        ///     </ul>
        /// </summary>
        /// <returns>
        ///     The method used to authenticate the connection.
        /// </returns>
        public string AuthenticationMethod
        {
            get
            {
                var prop = Connection.BindProperties;
                if (prop == null)
                {
                    return "simple";
                }

                return Connection.BindProperties.AuthenticationMethod;
            }
        }

        /// <summary>
        ///     Returns a copy of the set of constraints associated with this
        ///     connection. These constraints apply to all operations performed
        ///     through this connection (unless a different set of constraints is
        ///     specified when calling an operation method).
        /// </summary>
        /// <returns>
        ///     The set of default contraints that apply to this connection.
        /// </returns>
        /// <summary>
        ///     Sets the constraints that apply to all operations performed through
        ///     this connection (unless a different set of constraints is specified
        ///     when calling an operation method).  An LdapSearchConstraints object
        ///     which is passed to this method sets all constraints, while an
        ///     LdapConstraints object passed to this method sets only base constraints.
        /// </summary>
        /// <param name="cons">
        ///     An LdapConstraints or LdapSearchConstraints Object
        ///     containing the contstraint values to set.
        /// </param>
        /// <seealso cref="Constraints()">
        /// </seealso>
        /// <seealso cref="SearchConstraints()">
        /// </seealso>
        public LdapConstraints Constraints
        {
            get => (LdapConstraints)_defSearchCons.Clone();

            set
            {
                // Set all constraints, replace the object with a new one
                if (value is LdapSearchConstraints)
                {
                    _defSearchCons = (LdapSearchConstraints)value.Clone();
                    return;
                }

                // We set the constraints this way, so a thread doesn't get an
                // conconsistant view of the referrals.
                var newCons = (LdapSearchConstraints)_defSearchCons.Clone();
                newCons.HopLimit = value.HopLimit;
                newCons.TimeLimit = value.TimeLimit;
                newCons.setReferralHandler(value.getReferralHandler());
                newCons.ReferralFollowing = value.ReferralFollowing;
                var lsc = value.GetControls();
                if (lsc != null)
                {
                    newCons.SetControls(lsc);
                }

                var lp = newCons.Properties;
                if (lp != null)
                {
                    newCons.Properties = lp;
                }

                _defSearchCons = newCons;
            }
        }

        /// <summary>
        ///     Returns the host name of the Ldap server to which the object is or
        ///     was last connected, in the format originally specified.
        /// </summary>
        /// <returns>
        ///     The host name of the Ldap server to which the object last
        ///     connected or null if the object has never connected.
        /// </returns>
        public string Host => Connection.Host;

        /// <summary>
        ///     Returns the port number of the Ldap server to which the object is or
        ///     was last connected.
        /// </summary>
        /// <returns>
        ///     The port number of the Ldap server to which the object last
        ///     connected or -1 if the object has never connected.
        /// </returns>
        public int Port => Connection.Port;

        /// <summary>
        ///     Returns a copy of the set of search constraints associated with this
        ///     connection. These constraints apply to search operations performed
        ///     through this connection (unless a different set of
        ///     constraints is specified when calling the search operation method).
        /// </summary>
        /// <returns>
        ///     The set of default search contraints that apply to
        ///     this connection.
        /// </returns>
        /// <seealso cref="Constraints">
        /// </seealso>
        /// <seealso cref="LdapSearchConstraints">
        /// </seealso>
        public LdapSearchConstraints SearchConstraints => (LdapSearchConstraints)_defSearchCons.Clone();

        /// <summary>
        ///     Indicates whther the perform Secure Operation or not.
        /// </summary>
        /// <returns>
        ///     True if SSL is on
        ///     False if its not on.
        /// </returns>
        public bool SecureSocketLayer
        {
            get => Connection.Ssl;
            set => Connection.Ssl = value;
        }

        /// <summary>
        ///     Connection timeout in milliseconds, default is 0 which will use
        ///     the platform default timeout for TCP connections.
        /// </summary>
        /// <returns>
        ///     The timeout value in milliseconds.
        /// </returns>
        public int ConnectionTimeout
        {
            get => Connection.ConnectionTimeout;
            set => Connection.ConnectionTimeout = value;
        }

        /// <inheritdoc/>
        public bool Bound => Connection.Bound;

        /// <inheritdoc/>
        public bool Connected => Connection.Connected;

        /// <summary>
        ///     Indicates if the connection is protected by TLS.
        /// </summary>
        /// <returns>
        ///     If startTLS has completed this method returns true.
        ///     If stopTLS has completed or start tls failed, this method returns false.
        /// </returns>
        /// <returns>
        ///     True if the connection is protected by TLS.
        /// </returns>
        public bool Tls => Connection.Tls;

        /// <summary>
        ///     Returns the Server Controls associated with the most recent response
        ///     to a synchronous request on this connection object, or null
        ///     if the latest response contained no Server Controls. The method
        ///     always returns null for asynchronous requests. For asynchronous
        ///     requests, the response controls are available in LdapMessage.
        /// </summary>
        /// <returns>
        ///     The server controls associated with the most recent response
        ///     to a synchronous request or null if the response contains no server
        ///     controls.
        /// </returns>
        /// <seealso cref="LdapMessage.Controls">
        /// </seealso>
        public LdapControl[] ResponseControls
        {
            get
            {
                if (_responseCtls == null)
                {
                    return null;
                }

                // We have to clone the control just in case
                // we have two client threads that end up retreiving the
                // same control.
                var clonedControl = new LdapControl[_responseCtls.Length];

                // Also note we synchronize access to the local response
                // control object just in case another message containing controls
                // comes in from the server while we are busy duplicating
                // this one.
                lock (_responseCtlSemaphore)
                {
                    for (var i = 0; i < _responseCtls.Length; i++)
                    {
                        clonedControl[i] = (LdapControl)_responseCtls[i].Clone();
                    }
                }

                // Return the cloned copy.  Note we have still left the
                // control in the local responseCtls variable just in case
                // somebody requests it again.
                return clonedControl;
            }
        }

        /// <summary>
        ///     Return the Connection object associated with this LdapConnection.
        /// </summary>
        /// <returns>
        ///     the Connection object.
        /// </returns>
        private Connection Connection { get; set; }

        public void Dispose()
        {
            Dispose(true);
        }

        /// <inheritdoc />
        public void StartTls()
        {
            var startTls = MakeExtendedOperation(new LdapExtendedOperation(StartTlsOid, null), null);

            var tlsId = startTls.MessageId;

            Connection.AcquireWriteSemaphore(tlsId);
            try
            {
                if (!Connection.AreMessagesComplete())
                {
                    throw new LdapLocalException(
                        ExceptionMessages.OutstandingOperations,
                        LdapException.OperationsError);
                }

                // Stop reader when response to startTLS request received
                Connection.StopReaderOnReply(tlsId);

                // send tls message
                var queue = SendRequestToServer(startTls, _defSearchCons.TimeLimit, null, null);

                var response = (LdapExtendedResponse)queue.GetResponse();
                response.ChkResultCode();

                Connection.StartTls();
            }
            finally
            {
                // Free this semaphore no matter what exceptions get thrown
                Connection.FreeWriteSemaphore(tlsId);
            }
        }

        /// <inheritdoc />
        public void StopTls()
        {
            if (!Tls)
            {
                throw new LdapLocalException(ExceptionMessages.NoStarttls, LdapException.OperationsError);
            }

            var semaphoreId = Connection.AcquireWriteSemaphore();
            try
            {
                if (!Connection.AreMessagesComplete())
                {
                    throw new LdapLocalException(
                        ExceptionMessages.OutstandingOperations,
                        LdapException.OperationsError);
                }

                // stopTLS stops and starts the reader thread for us.
                Connection.StopTls();
            }
            finally
            {
                Connection.FreeWriteSemaphore(semaphoreId);
            }

            /* Now that the TLS socket is closed, reset everything.  This next
            line is temporary until JSSE is fixed to properly handle TLS stop */
            /* After stopTls the stream is very likely unusable */
            Connect(Host, Port);
        }

        /// <inheritdoc />
        public void Add(LdapEntry entry)
        {
            Add(entry, _defSearchCons);
        }

        /// <inheritdoc />
        public void Add(LdapEntry entry, LdapConstraints cons)
        {
            var queue = Add(entry, null, cons);

            // Get a handle to the add response
            var addResponse = (LdapResponse)queue.GetResponse();

            // Set local copy of responseControls synchronously if there were any
            lock (_responseCtlSemaphore)
            {
                _responseCtls = addResponse.Controls;
            }

            ChkResultCode(queue, cons, addResponse);
        }

        /// <inheritdoc />
        public void Bind(string dn, string passwd)
        {
            Bind(LdapV3, dn, passwd, _defSearchCons);
        }

        /// <inheritdoc />
        public void Bind(int version, string dn, string passwd)
        {
            Bind(version, dn, passwd, _defSearchCons);
        }

        /// <inheritdoc />
        public void Bind(string dn, string passwd, LdapConstraints cons)
        {
            Bind(LdapV3, dn, passwd, cons);
        }

        /// <inheritdoc />
        public void Bind(int version, string dn, string passwd, LdapConstraints cons)
        {
            byte[] pw = null;
            if (passwd != null)
            {
                pw = passwd.ToUtf8Bytes();
            }

            Bind(version, dn, pw, cons);
        }

        /// <inheritdoc />
        public void Bind(int version, string dn, byte[] passwd)
        {
            Bind(version, dn, passwd, _defSearchCons);
        }

        /// <inheritdoc />
        public void Bind(int version, string dn, byte[] passwd, LdapConstraints cons)
        {
            var queue = Bind(version, dn, passwd, null, cons);
            var res = (LdapResponse)queue.GetResponse();
            if (res != null)
            {
                // Set local copy of responseControls synchronously if any
                lock (_responseCtlSemaphore)
                {
                    _responseCtls = res.Controls;
                }

                ChkResultCode(queue, cons, res);
            }
        }

        /// <inheritdoc />
        public void Connect(string host, int port)
        {
            // connect doesn't affect other clones
            // If not a clone, destroys old connection.
            // Step through the space-delimited list
            var hostList = new Tokenizer(host, " ");
            while (hostList.HasMoreTokens())
            {
                try
                {
                    var specifiedPort = port;
                    var address = hostList.NextToken();
                    var colonIndex = address.IndexOf(':'); // after the colon is the port
                    if (colonIndex != -1 && colonIndex + 1 != address.Length)
                    {
                        // parse Port out of address
                        try
                        {
                            specifiedPort = int.Parse(address.Substring(colonIndex + 1));
                            address = address.Substring(0, colonIndex - 0);
                        }
                        catch (Exception e)
                        {
                            throw new ArgumentException(ExceptionMessages.InvalidAddress, e);
                        }
                    }

                    // This may return a different conn object
                    // Disassociate this clone with the underlying connection.
                    Connection = Connection.DestroyClone();
                    Connection.Connect(address, specifiedPort);
                    break;
                }
                catch (LdapException)
                {
                    if (!hostList.HasMoreTokens())
                    {
                        throw;
                    }
                }
            }
        }

        /// <inheritdoc />
        public void Delete(string dn)
        {
            Delete(dn, _defSearchCons);
        }

        /// <inheritdoc />
        public void Delete(string dn, LdapConstraints cons)
        {
            var queue = Delete(dn, null, cons);

            // Get a handle to the delete response
            var deleteResponse = (LdapResponse)queue.GetResponse();

            // Set local copy of responseControls synchronously - if there were any
            lock (_responseCtlSemaphore)
            {
                _responseCtls = deleteResponse.Controls;
            }

            ChkResultCode(queue, cons, deleteResponse);
        }

        /// <inheritdoc />
        public void Disconnect()
        {
            // disconnect from API call
            DisconnectImpl();
        }

        /// <inheritdoc />
        public LdapExtendedResponse ExtendedOperation(LdapExtendedOperation op)
        {
            return ExtendedOperation(op, _defSearchCons);
        }

        /// <inheritdoc />
        public LdapExtendedResponse ExtendedOperation(LdapExtendedOperation op, LdapConstraints cons)
        {
            // Call asynchronous API and get back handler to reponse queue
            var queue = ExtendedOperation(op, cons, null);
            var queueResponse = queue.GetResponse();
            var response = (LdapExtendedResponse)queueResponse;

            // Set local copy of responseControls synchronously - if there were any
            lock (_responseCtlSemaphore)
            {
                _responseCtls = response.Controls;
            }

            ChkResultCode(queue, cons, response);
            return response;
        }

        /// <inheritdoc />
        public void Modify(string dn, LdapModification mod)
        {
            Modify(dn, mod, _defSearchCons);
        }

        /// <inheritdoc />
        public void Modify(string dn, LdapModification mod, LdapConstraints cons)
        {
            var mods = new LdapModification[1];
            mods[0] = mod;
            Modify(dn, mods, cons);
        }

        /// <inheritdoc />
        public void Modify(string dn, LdapModification[] mods)
        {
            Modify(dn, mods, _defSearchCons);
        }

        /// <inheritdoc />
        public void Modify(string dn, LdapModification[] mods, LdapConstraints cons)
        {
            var queue = Modify(dn, mods, null, cons);

            // Get a handle to the modify response
            var modifyResponse = (LdapResponse)queue.GetResponse();

            // Set local copy of responseControls synchronously - if there were any
            lock (_responseCtlSemaphore)
            {
                _responseCtls = modifyResponse.Controls;
            }

            ChkResultCode(queue, cons, modifyResponse);
        }

        /// <inheritdoc />
        public LdapEntry Read(string dn)
        {
            return Read(dn, _defSearchCons);
        }

        /// <inheritdoc />
        public LdapEntry Read(string dn, LdapSearchConstraints cons)
        {
            return Read(dn, null, cons);
        }

        /// <inheritdoc />
        public LdapEntry Read(string dn, string[] attrs)
        {
            return Read(dn, attrs, _defSearchCons);
        }

        /// <inheritdoc />
        public LdapEntry Read(string dn, string[] attrs, LdapSearchConstraints cons)
        {
            var sr = Search(dn, ScopeBase, null, attrs, false, cons);

            if (!sr.HasMore())
            {
                return null;
            }

            var ret = sr.Next();
            if (sr.HasMore())
            {
                // "Read response is ambiguous, multiple entries returned"
                throw new LdapLocalException(ExceptionMessages.ReadMultiple, LdapException.AmbiguousResponse);
            }

            return ret;
        }

        /// <inheritdoc />
        public void Rename(string dn, string newRdn, bool deleteOldRdn)
        {
            Rename(dn, newRdn, deleteOldRdn, _defSearchCons);
        }

        /// <inheritdoc />
        public void Rename(string dn, string newRdn, bool deleteOldRdn, LdapConstraints cons)
        {
            // null for newParentdn means that this is originating as an Ldapv2 call
            Rename(dn, newRdn, null, deleteOldRdn, cons);
        }

        /// <inheritdoc />
        public void Rename(string dn, string newRdn, string newParentdn, bool deleteOldRdn)
        {
            Rename(dn, newRdn, newParentdn, deleteOldRdn, _defSearchCons);
        }

        /// <inheritdoc />
        public void Rename(string dn, string newRdn, string newParentdn, bool deleteOldRdn, LdapConstraints cons)
        {
            var queue = Rename(dn, newRdn, newParentdn, deleteOldRdn, null, cons);

            // Get a handle to the rename response
            var renameResponse = (LdapResponse)queue.GetResponse();

            // Set local copy of responseControls synchronously - if there were any
            lock (_responseCtlSemaphore)
            {
                _responseCtls = renameResponse.Controls;
            }

            ChkResultCode(queue, cons, renameResponse);
        }

        /// <inheritdoc />
        public ILdapSearchResults Search(string @base, int scope, string filter, string[] attrs, bool typesOnly)
        {
            return Search(@base, scope, filter, attrs, typesOnly, _defSearchCons);
        }

        /// <inheritdoc />
        public ILdapSearchResults Search(string @base, int scope, string filter, string[] attrs, bool typesOnly,
            LdapSearchConstraints cons)
        {
            var queue = Search(@base, scope, filter, attrs, typesOnly, null, cons);

            if (cons == null)
            {
                cons = _defSearchCons;
            }

            return new LdapSearchResults(this, queue, cons);
        }

        public event RemoteCertificateValidationCallback UserDefinedServerCertValidationDelegate
        {
            add => Connection.OnRemoteCertificateValidation += value;

            remove => Connection.OnRemoteCertificateValidation -= value;
        }

        public event LocalCertificateSelectionCallback UserDefinedClientCertSelectionDelegate
        {
            add => Connection.OnLocalCertificateSelection += value;

            remove => Connection.OnLocalCertificateSelection -= value;
        }

        /*
        * The following are methods that affect the operation of
        * LdapConnection, but are not Ldap requests.
        */

        /// <summary>
        ///     Returns a copy of the object with a private context, but sharing the
        ///     network connection if there is one.
        ///     The network connection remains open until all clones have
        ///     disconnected or gone out of scope. Any connection opened after
        ///     cloning is private to the object making the connection.
        ///     The clone can issue requests and freely modify options and search
        ///     constraints, and , without affecting the source object or other clones.
        ///     If the clone disconnects or reconnects, it is completely dissociated
        ///     from the source object and other clones. Reauthenticating in a clone,
        ///     however, is a global operation which will affect the source object
        ///     and all associated clones, because it applies to the single shared
        ///     physical connection. Any request by an associated object after one
        ///     has reauthenticated will carry the new identity.
        /// </summary>
        /// <returns>
        ///     A of the object.
        /// </returns>
        public object Clone()
        {
            LdapConnection newClone;
            object newObj;
            try
            {
                newObj = MemberwiseClone();
                newClone = (LdapConnection)newObj;
            }
            catch (Exception ce)
            {
                throw new Exception("Internal error, cannot create clone", ce);
            }

            newClone.Connection = Connection; // same underlying connection

            // now just duplicate the defSearchCons and responseCtls
            if (_defSearchCons != null)
            {
                newClone._defSearchCons = (LdapSearchConstraints)_defSearchCons.Clone();
            }
            else
            {
                newClone._defSearchCons = null;
            }

            if (_responseCtls != null)
            {
                newClone._responseCtls = new LdapControl[_responseCtls.Length];
                for (var i = 0; i < _responseCtls.Length; i++)
                {
                    newClone._responseCtls[i] = (LdapControl)_responseCtls[i].Clone();
                }
            }
            else
            {
                newClone._responseCtls = null;
            }

            Connection.IncrCloneCount(); // Increment the count of clones
            return newObj;
        }

        private void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                DisconnectImpl();
            }
        }

        /// <summary>
        ///     Returns a property of a connection object.
        /// </summary>
        /// <param name="name">
        ///     Name of the property to be returned.
        ///     The following read-only properties are available
        ///     for any given connection:
        ///     <ul>
        ///         <li>
        ///             Ldap_PROPERTY_SDK returns the version of this SDK,
        ///             as a Float data type.
        ///         </li>
        ///         <li>
        ///             Ldap_PROPERTY_PROTOCOL returns the highest supported version of
        ///             the Ldap protocol, as a Float data type.
        ///         </li>
        ///         <li>
        ///             Ldap_PROPERTY_SECURITY returns a comma-separated list of the
        ///             types of authentication supported, as a
        ///             string.
        ///         </li>
        ///     </ul>
        ///     A deep copy of the property is provided where applicable; a
        ///     client does not need to clone the object received.
        /// </param>
        /// <returns>
        ///     The object associated with the requested property,
        ///     or null if the property is not defined.
        /// </returns>
        /// <seealso cref="LdapConstraints.GetProperty">
        /// </seealso>
        /// <seealso cref="object">
        /// </seealso>
        public object GetProperty(string name)
        {
            if (name.EqualsOrdinalCI(LdapPropertySdk))
            {
                return Connection.Sdk;
            }

            if (name.EqualsOrdinalCI(LdapPropertyProtocol))
            {
                return Connection.Protocol;
            }

            if (name.EqualsOrdinalCI(LdapPropertySecurity))
            {
                return Connection.Security;
            }

            return null;
        }

        /// <summary>
        ///     Registers an object to be notified on arrival of an unsolicited
        ///     message from a server.
        ///     An unsolicited message has the ID 0. A new thread is created and
        ///     the method "messageReceived" in each registered object is called in
        ///     turn.
        /// </summary>
        /// <param name="listener">
        ///     An object to be notified on arrival of an
        ///     unsolicited message from a server.  This object must
        ///     implement the LdapUnsolicitedNotificationListener interface.
        /// </param>
        public void AddUnsolicitedNotificationListener(ILdapUnsolicitedNotificationListener listener)
        {
            if (listener != null)
            {
                Connection.AddUnsolicitedNotificationListener(listener);
            }
        }

        /// <summary>
        ///     Deregisters an object so that it will no longer be notified on
        ///     arrival of an unsolicited message from a server. If the object is
        ///     null or was not previously registered for unsolicited notifications,
        ///     the method does nothing.
        /// </summary>
        /// <param name="listener">
        ///     An object to no longer be notified on arrival of
        ///     an unsolicited message from a server.
        /// </param>
        public void RemoveUnsolicitedNotificationListener(ILdapUnsolicitedNotificationListener listener)
        {
            if (listener != null)
            {
                Connection.RemoveUnsolicitedNotificationListener(listener);
            }
        }

        // *************************************************************************
        // Below are all of the Ldap protocol operation methods
        // *************************************************************************

        // *************************************************************************
        // abandon methods
        // *************************************************************************

        /// <summary>
        ///     Notifies the server not to send additional results associated with
        ///     this LdapSearchResults object, and discards any results already
        ///     received.
        /// </summary>
        /// <param name="results">
        ///     An object returned from a search.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public void Abandon(LdapSearchResults results)
        {
            Abandon(results, _defSearchCons);
        }

        /// <summary>
        ///     Notifies the server not to send additional results associated with
        ///     this LdapSearchResults object, and discards any results already
        ///     received.
        /// </summary>
        /// <param name="results">
        ///     An object returned from a search.
        /// </param>
        /// <param name="cons">
        ///     The contraints specific to the operation.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public void Abandon(LdapSearchResults results, LdapConstraints cons)
        {
            results.Abandon();
        }

        /// <summary>
        ///     Abandons an asynchronous operation.
        /// </summary>
        /// <param name="id">
        ///     The ID of the asynchronous operation to abandon. The ID
        ///     can be obtained from the response queue for the
        ///     operation.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public void Abandon(int id)
        {
            Abandon(id, _defSearchCons);
        }

        /// <summary>
        ///     Abandons an asynchronous operation, using the specified
        ///     constraints.
        /// </summary>
        /// <param name="id">
        ///     The ID of the asynchronous operation to abandon.
        ///     The ID can be obtained from the search
        ///     queue for the operation.
        /// </param>
        /// <param name="cons">
        ///     The contraints specific to the operation.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public void Abandon(int id, LdapConstraints cons)
        {
            // We need to inform the Message Agent which owns this messageID to
            // remove it from the queue.
            try
            {
                var agent = Connection.GetMessageAgent(id);
                agent.Abandon(id, cons);
            }
            catch (FieldAccessException fae)
            {
                Logger.Warn("Exception swallowed", fae);
            }
        }

        /// <summary>
        ///     Abandons all outstanding operations managed by the queue.
        ///     All operations in progress, which are managed by the specified queue,
        ///     are abandoned.
        /// </summary>
        /// <param name="queue">
        ///     The queue returned from an asynchronous request.
        ///     All outstanding operations managed by the queue
        ///     are abandoned, and the queue is emptied.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public void Abandon(LdapMessageQueue queue)
        {
            Abandon(queue, _defSearchCons);
        }

        /// <summary>
        ///     Abandons all outstanding operations managed by the queue.
        ///     All operations in progress, which are managed by the specified
        ///     queue, are abandoned.
        /// </summary>
        /// <param name="queue">
        ///     The queue returned from an asynchronous request.
        ///     All outstanding operations managed by the queue
        ///     are abandoned, and the queue is emptied.
        /// </param>
        /// <param name="cons">
        ///     The contraints specific to the operation.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public void Abandon(LdapMessageQueue queue, LdapConstraints cons)
        {
            if (queue != null)
            {
                MessageAgent agent;
                if (queue is LdapSearchQueue)
                {
                    agent = queue.MessageAgent;
                }
                else
                {
                    agent = queue.MessageAgent;
                }

                var msgIds = agent.MessageIDs;
                for (var i = 0; i < msgIds.Length; i++)
                {
                    agent.Abandon(msgIds[i], cons);
                }
            }
        }

        /// <summary>
        ///     Asynchronously adds an entry to the directory.
        /// </summary>
        /// <param name="entry">
        ///     LdapEntry object specifying the distinguished
        ///     name and attributes of the new entry.
        /// </param>
        /// <param name="queue">
        ///     Handler for messages returned from a server in
        ///     response to this request. If it is null, a
        ///     queue object is created internally.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public LdapResponseQueue Add(LdapEntry entry, LdapResponseQueue queue)
        {
            return Add(entry, queue, _defSearchCons);
        }

        /// <summary>
        ///     Asynchronously adds an entry to the directory, using the specified
        ///     constraints.
        /// </summary>
        /// <param name="entry">
        ///     LdapEntry object specifying the distinguished
        ///     name and attributes of the new entry.
        /// </param>
        /// <param name="queue">
        ///     Handler for messages returned from a server in
        ///     response to this request. If it is null, a
        ///     queue object is created internally.
        /// </param>
        /// <param name="cons">
        ///     Constraints specific to the operation.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public LdapResponseQueue Add(LdapEntry entry, LdapResponseQueue queue, LdapConstraints cons)
        {
            if (cons == null)
            {
                cons = _defSearchCons;
            }

            // error check the parameters
            if (entry == null)
            {
                throw new ArgumentException("The LdapEntry parameter" + " cannot be null");
            }

            if (entry.Dn == null)
            {
                throw new ArgumentException("The DN value must be present" + " in the LdapEntry object");
            }

            LdapMessage msg = new LdapAddRequest(entry, cons.GetControls());

            return SendRequestToServer(msg, cons.TimeLimit, queue, null);
        }

        /// <summary>
        ///     Asynchronously authenticates to the Ldap server (that the object is
        ///     currently connected to) using the specified name, password, Ldap
        ///     version, and queue.
        ///     If the object has been disconnected from an Ldap server,
        ///     this method attempts to reconnect to the server. If the object
        ///     has already authenticated, the old authentication is discarded.
        /// </summary>
        /// <param name="version">
        ///     The Ldap protocol version, use Ldap_V3.
        ///     Ldap_V2 is not supported.
        /// </param>
        /// <param name="dn">
        ///     If non-null and non-empty, specifies that the
        ///     connection and all operations through it should
        ///     be authenticated with dn as the distinguished
        ///     name.
        /// </param>
        /// <param name="passwd">
        ///     If non-null and non-empty, specifies that the
        ///     connection and all operations through it should
        ///     be authenticated with dn as the distinguished
        ///     name and passwd as password.
        /// </param>
        /// <param name="queue">
        ///     Handler for messages returned from a server in
        ///     response to this request. If it is null, a
        ///     queue object is created internally.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public LdapResponseQueue Bind(int version, string dn, byte[] passwd, LdapResponseQueue queue)
        {
            return Bind(version, dn, passwd, queue, _defSearchCons);
        }

        /// <summary>
        ///     Asynchronously authenticates to the Ldap server (that the object is
        ///     currently connected to) using the specified name, password, Ldap
        ///     version, queue, and constraints.
        ///     If the object has been disconnected from an Ldap server,
        ///     this method attempts to reconnect to the server. If the object
        ///     had already authenticated, the old authentication is discarded.
        /// </summary>
        /// <param name="version">
        ///     The Ldap protocol version, use Ldap_V3.
        ///     Ldap_V2 is not supported.
        /// </param>
        /// <param name="dn">
        ///     If non-null and non-empty, specifies that the
        ///     connection and all operations through it should
        ///     be authenticated with dn as the distinguished
        ///     name.
        /// </param>
        /// <param name="passwd">
        ///     If non-null and non-empty, specifies that the
        ///     connection and all operations through it should
        ///     be authenticated with dn as the distinguished
        ///     name and passwd as password.
        /// </param>
        /// <param name="queue">
        ///     Handler for messages returned from a server in
        ///     response to this request. If it is null, a
        ///     queue object is created internally.
        /// </param>
        /// <param name="cons">
        ///     Constraints specific to the operation.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public LdapResponseQueue Bind(int version, string dn, byte[] passwd, LdapResponseQueue queue,
            LdapConstraints cons)
        {
            if (cons == null)
            {
                cons = _defSearchCons;
            }

            if (dn == null)
            {
                dn = string.Empty;
            }
            else
            {
                dn = dn.Trim();
            }

            if (passwd == null)
            {
                passwd = new byte[] { };
            }

            var anonymous = false;
            if (passwd.Length == 0)
            {
                anonymous = true; // anonymous, passwd length zero with simple bind
                dn = string.Empty; // set to null if anonymous
            }

            LdapMessage msg = new LdapBindRequest(version, dn, passwd, cons.GetControls());

            var msgId = msg.MessageId;
            var bindProps = new BindProperties(version, dn, "simple", anonymous, null);

            // For bind requests, if not connected, attempt to reconnect
            if (!Connection.Connected)
            {
                if (Connection.Host != null)
                {
                    Connection.Connect(Connection.Host, Connection.Port);
                }
                else
                {
                    throw new LdapException(ExceptionMessages.ConnectionImpossible, LdapException.ConnectError, null);
                }
            }

            // The semaphore is released when the bind response is queued.
            Connection.AcquireWriteSemaphore(msgId);

            return SendRequestToServer(msg, cons.TimeLimit, queue, bindProps);
        }

        // *************************************************************************
        // compare methods
        // *************************************************************************

        /// <summary>
        ///     Synchronously checks to see if an entry contains an attribute
        ///     with a specified value.
        /// </summary>
        /// <param name="dn">
        ///     The distinguished name of the entry to use in the
        ///     comparison.
        /// </param>
        /// <param name="attr">
        ///     The attribute to compare against the entry. The
        ///     method checks to see if the entry has an
        ///     attribute with the same name and value as this
        ///     attribute.
        /// </param>
        /// <returns>
        ///     True if the entry has the value,
        ///     and false if the entry does not
        ///     have the value or the attribute.
        /// </returns>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public bool Compare(string dn, LdapAttribute attr)
        {
            return Compare(dn, attr, _defSearchCons);
        }

        /// <summary>
        ///     Synchronously checks to see if an entry contains an attribute with a
        ///     specified value, using the specified constraints.
        /// </summary>
        /// <param name="dn">
        ///     The distinguished name of the entry to use in the
        ///     comparison.
        /// </param>
        /// <param name="attr">
        ///     The attribute to compare against the entry. The
        ///     method checks to see if the entry has an
        ///     attribute with the same name and value as this
        ///     attribute.
        /// </param>
        /// <param name="cons">
        ///     Constraints specific to the operation.
        /// </param>
        /// <returns>
        ///     True if the entry has the value,
        ///     and false if the entry does not
        ///     have the value or the attribute.
        /// </returns>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public bool Compare(string dn, LdapAttribute attr, LdapConstraints cons)
        {
            var ret = false;

            var queue = Compare(dn, attr, null, cons);

            var res = (LdapResponse)queue.GetResponse();

            // Set local copy of responseControls synchronously - if there were any
            lock (_responseCtlSemaphore)
            {
                _responseCtls = res.Controls;
            }

            if (res.ResultCode == LdapException.CompareTrue)
            {
                ret = true;
            }
            else if (res.ResultCode == LdapException.CompareFalse)
            {
                ret = false;
            }
            else
            {
                ChkResultCode(queue, cons, res);
            }

            return ret;
        }

        /// <summary>
        ///     Asynchronously compares an attribute value with one in the directory,
        ///     using the specified queue.
        ///     Please note that a successful completion of this command results in
        ///     one of two status codes: LdapException.COMPARE_TRUE if the entry
        ///     has the value, and LdapException.COMPARE_FALSE if the entry
        ///     does not have the value or the attribute.
        /// </summary>
        /// <param name="dn">
        ///     The distinguished name of the entry containing an
        ///     attribute to compare.
        /// </param>
        /// <param name="attr">
        ///     An attribute to compare.
        /// </param>
        /// <param name="queue">
        ///     The queue for messages returned from a server in
        ///     response to this request. If it is null, a
        ///     queue object is created internally.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        /// <seealso cref="LdapException.CompareTrue">
        /// </seealso>
        /// <seealso cref="LdapException.CompareFalse">
        /// </seealso>
        public LdapResponseQueue Compare(string dn, LdapAttribute attr, LdapResponseQueue queue)
        {
            return Compare(dn, attr, queue, _defSearchCons);
        }

        /// <summary>
        ///     Asynchronously compares an attribute value with one in the directory,
        ///     using the specified queue and contraints.
        ///     Please note that a successful completion of this command results in
        ///     one of two status codes: LdapException.COMPARE_TRUE if the entry
        ///     has the value, and LdapException.COMPARE_FALSE if the entry
        ///     does not have the value or the attribute.
        /// </summary>
        /// <param name="dn">
        ///     The distinguished name of the entry containing an
        ///     attribute to compare.
        /// </param>
        /// <param name="attr">
        ///     An attribute to compare.
        /// </param>
        /// <param name="queue">
        ///     Handler for messages returned from a server in
        ///     response to this request. If it is null, a
        ///     queue object is created internally.
        /// </param>
        /// <param name="cons">
        ///     Constraints specific to the operation.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        /// <seealso cref="LdapException.CompareTrue">
        /// </seealso>
        /// <seealso cref="LdapException.CompareFalse">
        /// </seealso>
        public LdapResponseQueue Compare(string dn, LdapAttribute attr, LdapResponseQueue queue,
            LdapConstraints cons)
        {
            if (attr.Size() != 1)
            {
                throw new ArgumentException("compare: Exactly one value " + "must be present in the LdapAttribute");
            }

            if (dn == null)
            {
                // Invalid parameter
                throw new ArgumentException("compare: DN cannot be null");
            }

            if (cons == null)
            {
                cons = _defSearchCons;
            }

            LdapMessage msg = new LdapCompareRequest(dn, attr.Name, attr.ByteValue, cons.GetControls());

            return SendRequestToServer(msg, cons.TimeLimit, queue, null);
        }

        /// <summary>
        ///     Asynchronously deletes the entry with the specified distinguished name
        ///     from the directory and returns the results to the specified queue.
        ///     Note: A Delete operation will not remove an entry that contains
        ///     subordinate entries, nor will it dereference alias entries.
        /// </summary>
        /// <param name="dn">
        ///     The distinguished name of the entry to modify.
        /// </param>
        /// <param name="queue">
        ///     The queue for messages returned from a server in
        ///     response to this request. If it is null, a
        ///     queue object is created internally.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public LdapResponseQueue Delete(string dn, LdapResponseQueue queue)
        {
            return Delete(dn, queue, _defSearchCons);
        }

        /// <summary>
        ///     Asynchronously deletes the entry with the specified distinguished name
        ///     from the directory, using the specified contraints and queue.
        ///     Note: A Delete operation will not remove an entry that contains
        ///     subordinate entries, nor will it dereference alias entries.
        /// </summary>
        /// <param name="dn">
        ///     The distinguished name of the entry to delete.
        /// </param>
        /// <param name="queue">
        ///     The queue for messages returned from a server in
        ///     response to this request. If it is null, a
        ///     queue object is created internally.
        /// </param>
        /// <param name="cons">
        ///     The constraints specific to the operation.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public LdapResponseQueue Delete(string dn, LdapResponseQueue queue, LdapConstraints cons)
        {
            if (dn == null)
            {
                // Invalid DN parameter
                throw new ArgumentException(ExceptionMessages.DnParamError);
            }

            if (cons == null)
            {
                cons = _defSearchCons;
            }

            LdapMessage msg = new LdapDeleteRequest(dn, cons.GetControls());

            return SendRequestToServer(msg, cons.TimeLimit, queue, null);
        }

        /// <summary>
        ///     Synchronously disconnect from the server.
        /// </summary>
        /// <param name="how">
        ///     true if application call disconnect API, false if finalize.
        /// </param>
        private void DisconnectImpl()
        {
            if (Connection.Tls)
            {
                StopTls();
            }

            // disconnect doesn't affect other clones
            // If not a clone, destroys connection
            Connection = Connection.DestroyClone();
        }

        /*
        * Asynchronous Ldap extended request
        */

        /// <summary>
        ///     Provides an asynchronous means to access extended, non-mandatory
        ///     operations offered by a particular Ldapv3 compliant server.
        /// </summary>
        /// <param name="op">
        ///     The object which contains (1) an identifier of an extended
        ///     operation which should be recognized by the particular Ldap
        ///     server this client is connected to and (2) an
        ///     operation-specific sequence of octet strings
        ///     or BER-encoded values.
        /// </param>
        /// <param name="queue">
        ///     The queue for messages returned from a server in
        ///     response to this request. If it is null, a queue
        ///     object is created internally.
        /// </param>
        /// <returns>
        ///     An operation-specific object, containing an ID and either an octet
        ///     string or BER-encoded values.
        /// </returns>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public LdapResponseQueue ExtendedOperation(LdapExtendedOperation op, LdapResponseQueue queue)
        {
            return ExtendedOperation(op, _defSearchCons, queue);
        }

        /*
        *  Asynchronous Ldap extended request with SearchConstraints
        */

        /// <summary>
        ///     Provides an asynchronous means to access extended, non-mandatory
        ///     operations offered by a particular Ldapv3 compliant server.
        /// </summary>
        /// <param name="op">
        ///     The object which contains (1) an identifier of an extended
        ///     operation which should be recognized by the particular Ldap
        ///     server this client is connected to and (2) an operation-
        ///     specific sequence of octet strings or BER-encoded values.
        /// </param>
        /// <param name="queue">
        ///     The queue for messages returned from a server in
        ///     response to this request. If it is null, a queue
        ///     object is created internally.
        /// </param>
        /// <param name="cons">
        ///     The constraints specific to this operation.
        /// </param>
        /// <returns>
        ///     An operation-specific object, containing an ID and either an
        ///     octet string or BER-encoded values.
        /// </returns>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public LdapResponseQueue ExtendedOperation(LdapExtendedOperation op, LdapConstraints cons,
            LdapResponseQueue queue)
        {
            // Use default constraints if none-specified
            if (cons == null)
            {
                cons = _defSearchCons;
            }

            var msg = MakeExtendedOperation(op, cons);
            return SendRequestToServer(msg, cons.TimeLimit, queue, null);
        }

        /// <summary>
        ///     Formulates the extended operation, constraints into an
        ///     LdapMessage and returns the LdapMessage.  This is used by
        ///     extendedOperation and startTLS which needs the LdapMessage to
        ///     get the MessageID.
        /// </summary>
        internal LdapMessage MakeExtendedOperation(LdapExtendedOperation op, LdapConstraints cons)
        {
            // Use default constraints if none-specified
            if (cons == null)
            {
                cons = _defSearchCons;
            }

            // error check the parameters
            if (op.GetId() == null)
            {
                // Invalid extended operation parameter, no OID specified
                throw new ArgumentException(ExceptionMessages.OpParamError);
            }

            return new LdapExtendedRequest(op, cons.GetControls());
        }

        /// <summary>
        ///     Asynchronously makes a single change to an existing entry in the
        ///     directory.
        ///     For example, this modify method can change the value of an attribute,
        ///     add a new attribute value, or remove an existing attribute value.
        ///     The LdapModification object specifies both the change to be made and
        ///     the LdapAttribute value to be changed.
        ///     If the request fails with {@link LdapException.CONNECT_ERROR},
        ///     it is indeterminate whether or not the server made the modification.
        /// </summary>
        /// <param name="dn">
        ///     Distinguished name of the entry to modify.
        /// </param>
        /// <param name="mod">
        ///     A single change to be made to the entry.
        /// </param>
        /// <param name="queue">
        ///     Handler for messages returned from a server in
        ///     response to this request. If it is null, a
        ///     queue object is created internally.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public LdapResponseQueue Modify(string dn, LdapModification mod, LdapResponseQueue queue)
        {
            return Modify(dn, mod, queue, _defSearchCons);
        }

        /// <summary>
        ///     Asynchronously makes a single change to an existing entry in the
        ///     directory, using the specified constraints and queue.
        ///     For example, this modify method can change the value of an attribute,
        ///     add a new attribute value, or remove an existing attribute value.
        ///     The LdapModification object specifies both the change to be made
        ///     and the LdapAttribute value to be changed.
        ///     If the request fails with {@link LdapException.CONNECT_ERROR},
        ///     it is indeterminate whether or not the server made the modification.
        /// </summary>
        /// <param name="dn">
        ///     Distinguished name of the entry to modify.
        /// </param>
        /// <param name="mod">
        ///     A single change to be made to the entry.
        /// </param>
        /// <param name="queue">
        ///     Handler for messages returned from a server in
        ///     response to this request. If it is null, a
        ///     queue object is created internally.
        /// </param>
        /// <param name="cons">
        ///     Constraints specific to the operation.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public LdapResponseQueue Modify(string dn, LdapModification mod, LdapResponseQueue queue,
            LdapConstraints cons)
        {
            var mods = new LdapModification[1];
            mods[0] = mod;
            return Modify(dn, mods, queue, cons);
        }

        /// <summary>
        ///     Asynchronously makes a set of changes to an existing entry in the
        ///     directory.
        ///     For example, this modify method can change attribute values, add new
        ///     attribute values, or remove existing attribute values.
        ///     Because the server applies all changes in an LdapModification array
        ///     atomically, the application can expect that no changes
        ///     have been performed if an error is returned.
        ///     If the request fails with {@link LdapException.CONNECT_ERROR},
        ///     it is indeterminate whether or not the server made the modifications.
        /// </summary>
        /// <param name="dn">
        ///     The distinguished name of the entry to modify.
        /// </param>
        /// <param name="mods">
        ///     The changes to be made to the entry.
        /// </param>
        /// <param name="queue">
        ///     The queue for messages returned from a server in
        ///     response to this request. If it is null, a
        ///     queue object is created internally.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public LdapResponseQueue Modify(string dn, LdapModification[] mods, LdapResponseQueue queue)
        {
            return Modify(dn, mods, queue, _defSearchCons);
        }

        /// <summary>
        ///     Asynchronously makes a set of changes to an existing entry in the
        ///     directory, using the specified constraints and queue.
        ///     For example, this modify method can change attribute values, add new
        ///     attribute values, or remove existing attribute values.
        ///     Because the server applies all changes in an LdapModification array
        ///     atomically, the application can expect that no changes
        ///     have been performed if an error is returned.
        ///     If the request fails with {@link LdapException.CONNECT_ERROR},
        ///     it is indeterminate whether or not the server made the modifications.
        /// </summary>
        /// <param name="dn">
        ///     The distinguished name of the entry to modify.
        /// </param>
        /// <param name="mods">
        ///     The changes to be made to the entry.
        /// </param>
        /// <param name="queue">
        ///     The queue for messages returned from a server in
        ///     response to this request. If it is null, a
        ///     queue object is created internally.
        /// </param>
        /// <param name="cons">
        ///     Constraints specific to the operation.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public LdapResponseQueue Modify(string dn, LdapModification[] mods, LdapResponseQueue queue,
            LdapConstraints cons)
        {
            if (dn == null)
            {
                // Invalid DN parameter
                throw new ArgumentException(ExceptionMessages.DnParamError);
            }

            if (cons == null)
            {
                cons = _defSearchCons;
            }

            LdapMessage msg = new LdapModifyRequest(dn, mods, cons.GetControls());

            return SendRequestToServer(msg, cons.TimeLimit, queue, null);
        }

        /// <summary>
        ///     Synchronously reads the entry specified by the Ldap URL.
        ///     When this read method is called, a new connection is created
        ///     automatically, using the host and port specified in the URL. After
        ///     finding the entry, the method closes the connection (in other words,
        ///     it disconnects from the Ldap server).
        ///     If the URL specifies a filter and scope, they are not used. Of the
        ///     information specified in the URL, this method only uses the Ldap host
        ///     name and port number, the base distinguished name (DN), and the list
        ///     of attributes to return.
        /// </summary>
        /// <param name="toGet">
        ///     Ldap URL specifying the entry to read.
        /// </param>
        /// <returns>
        ///     The entry specified by the base DN.
        /// </returns>
        /// <exception>
        ///     LdapException if the object was not found.
        /// </exception>
        public static LdapEntry Read(LdapUrl toGet)
        {
            return Read(toGet, null);
        }

        /// <summary>
        ///     Synchronously reads the entry specified by the Ldap URL, using the
        ///     specified constraints.
        ///     When this method is called, a new connection is created
        ///     automatically, using the host and port specified in the URL. After
        ///     finding the entry, the method closes the connection (in other words,
        ///     it disconnects from the Ldap server).
        ///     If the URL specifies a filter and scope, they are not used. Of the
        ///     information specified in the URL, this method only uses the Ldap host
        ///     name and port number, the base distinguished name (DN), and the list
        ///     of attributes to return.
        /// </summary>
        /// <returns>
        ///     The entry specified by the base DN.
        /// </returns>
        /// <param name="toGet">
        ///     Ldap URL specifying the entry to read.
        /// </param>
        /// <param name="cons">
        ///     Constraints specific to the operation.
        /// </param>
        /// <exception>
        ///     LdapException if the object was not found.
        /// </exception>
        public static LdapEntry Read(LdapUrl toGet, LdapSearchConstraints cons)
        {
            using (var lconn = new LdapConnection())
            {
                lconn.Connect(toGet.Host, toGet.Port);
                var toReturn = lconn.Read(toGet.GetDn(), toGet.AttributeArray, cons);
                return toReturn;
            }
        }

        /*
        * rename
        */

        /// <summary>
        ///     Asynchronously renames an existing entry in the directory.
        /// </summary>
        /// <param name="dn">
        ///     The current distinguished name of the entry.
        /// </param>
        /// <param name="newRdn">
        ///     The new relative distinguished name for the entry.
        /// </param>
        /// <param name="deleteOldRdn">
        ///     If true, the old name is not retained as an
        ///     attribute value. If false, the old name is
        ///     retained as an attribute value.
        /// </param>
        /// <param name="queue">
        ///     The queue for messages returned from a server in
        ///     response to this request. If it is null, a
        ///     queue object is created internally.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public LdapResponseQueue Rename(string dn, string newRdn, bool deleteOldRdn, LdapResponseQueue queue)
        {
            return Rename(dn, newRdn, deleteOldRdn, queue, _defSearchCons);
        }

        /// <summary>
        ///     Asynchronously renames an existing entry in the directory, using the
        ///     specified constraints.
        /// </summary>
        /// <param name="dn">
        ///     The current distinguished name of the entry.
        /// </param>
        /// <param name="newRdn">
        ///     The new relative distinguished name for the entry.
        /// </param>
        /// <param name="deleteOldRdn">
        ///     If true, the old name is not retained as an
        ///     attribute value. If false, the old name is
        ///     retained as an attribute value.
        /// </param>
        /// <param name="queue">
        ///     The queue for messages returned from a server in
        ///     response to this request. If it is null, a
        ///     queue object is created internally.
        /// </param>
        /// <param name="cons">
        ///     The constraints specific to the operation.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public LdapResponseQueue Rename(string dn, string newRdn, bool deleteOldRdn, LdapResponseQueue queue,
            LdapConstraints cons)
        {
            return Rename(dn, newRdn, null, deleteOldRdn, queue, cons);
        }

        /// <summary>
        ///     Asynchronously renames an existing entry in the directory, possibly
        ///     repositioning the entry in the directory.
        /// </summary>
        /// <param name="dn">
        ///     The current distinguished name of the entry.
        /// </param>
        /// <param name="newRdn">
        ///     The new relative distinguished name for the entry.
        /// </param>
        /// <param name="newParentdn">
        ///     The distinguished name of an existing entry which
        ///     is to be the new parent of the entry.
        /// </param>
        /// <param name="deleteOldRdn">
        ///     If true, the old name is not retained as an
        ///     attribute value. If false, the old name is
        ///     retained as an attribute value.
        /// </param>
        /// <param name="queue">
        ///     The queue for messages returned from a server in
        ///     response to this request. If it is null, a
        ///     queue object is created internally.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public LdapResponseQueue Rename(string dn, string newRdn, string newParentdn, bool deleteOldRdn,
            LdapResponseQueue queue)
        {
            return Rename(dn, newRdn, newParentdn, deleteOldRdn, queue, _defSearchCons);
        }

        /// <summary>
        ///     Asynchronously renames an existing entry in the directory, using the
        ///     specified constraints and possibly repositioning the entry in the
        ///     directory.
        /// </summary>
        /// <param name="dn">
        ///     The current distinguished name of the entry.
        /// </param>
        /// <param name="newRdn">
        ///     The new relative distinguished name for the entry.
        /// </param>
        /// <param name="newParentdn">
        ///     The distinguished name of an existing entry which
        ///     is to be the new parent of the entry.
        /// </param>
        /// <param name="deleteOldRdn">
        ///     If true, the old name is not retained as an
        ///     attribute value. If false, the old name is
        ///     retained as an attribute value.
        /// </param>
        /// <param name="queue">
        ///     The queue for messages returned from a server in
        ///     response to this request. If it is null, a
        ///     queue object is created internally.
        /// </param>
        /// <param name="cons">
        ///     The constraints specific to the operation.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public LdapResponseQueue Rename(string dn, string newRdn, string newParentdn, bool deleteOldRdn,
            LdapResponseQueue queue, LdapConstraints cons)
        {
            if (dn == null || newRdn == null)
            {
                // Invalid DN or RDN parameter
                throw new ArgumentException(ExceptionMessages.RdnParamError);
            }

            if (cons == null)
            {
                cons = _defSearchCons;
            }

            LdapMessage msg = new LdapModifyDnRequest(dn, newRdn, newParentdn, deleteOldRdn, cons.GetControls());

            return SendRequestToServer(msg, cons.TimeLimit, queue, null);
        }

        /// <summary>
        ///     Asynchronously performs the search specified by the parameters.
        /// </summary>
        /// <param name="base">
        ///     The base distinguished name to search from.
        /// </param>
        /// <param name="scope">
        ///     The scope of the entries to search. The following
        ///     are the valid options:.
        ///     <ul>
        ///         <li>SCOPE_BASE - searches only the base DN</li>
        ///         <li>SCOPE_ONE - searches only entries under the base DN</li>
        ///         <li>
        ///             SCOPE_SUB - searches the base DN and all entries
        ///             within its subtree
        ///         </li>
        ///     </ul>
        /// </param>
        /// <param name="filter">
        ///     Search filter specifying the search criteria.
        /// </param>
        /// <param name="attrs">
        ///     Names of attributes to retrieve.
        /// </param>
        /// <param name="typesOnly">
        ///     If true, returns the names but not the values of
        ///     the attributes found.  If false, returns the
        ///     names and values for attributes found.
        /// </param>
        /// <param name="queue">
        ///     Handler for messages returned from a server in
        ///     response to this request. If it is null, a
        ///     queue object is created internally.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public LdapSearchQueue Search(string @base, int scope, string filter, string[] attrs, bool typesOnly,
            LdapSearchQueue queue)
        {
            return Search(@base, scope, filter, attrs, typesOnly, queue, _defSearchCons);
        }

        /// <summary>
        ///     Asynchronously performs the search specified by the parameters,
        ///     also allowing specification of constraints for the search (such
        ///     as the maximum number of entries to find or the maximum time to
        ///     wait for search results).
        /// </summary>
        /// <param name="base">
        ///     The base distinguished name to search from.
        /// </param>
        /// <param name="scope">
        ///     The scope of the entries to search. The following
        ///     are the valid options:.
        ///     <ul>
        ///         <li>SCOPE_BASE - searches only the base DN</li>
        ///         <li>SCOPE_ONE - searches only entries under the base DN</li>
        ///         <li>
        ///             SCOPE_SUB - searches the base DN and all entries
        ///             within its subtree
        ///         </li>
        ///     </ul>
        /// </param>
        /// <param name="filter">
        ///     The search filter specifying the search criteria.
        /// </param>
        /// <param name="attrs">
        ///     The names of attributes to retrieve.
        /// </param>
        /// <param name="typesOnly">
        ///     If true, returns the names but not the values of
        ///     the attributes found.  If false, returns the
        ///     names and values for attributes found.
        /// </param>
        /// <param name="queue">
        ///     The queue for messages returned from a server in
        ///     response to this request. If it is null, a
        ///     queue object is created internally.
        /// </param>
        /// <param name="cons">
        ///     The constraints specific to the search.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public LdapSearchQueue Search(string @base, int scope, string filter, string[] attrs, bool typesOnly,
            LdapSearchQueue queue, LdapSearchConstraints cons)
        {
            if (filter == null)
            {
                filter = "objectclass=*";
            }

            if (cons == null)
            {
                cons = _defSearchCons;
            }

            LdapMessage msg = new LdapSearchRequest(@base, scope, filter, attrs, cons.Dereference, cons.MaxResults,
                cons.ServerTimeLimit, typesOnly, cons.GetControls());
            MessageAgent agent;
            var myqueue = queue;
            if (myqueue == null)
            {
                agent = new MessageAgent();
                myqueue = new LdapSearchQueue(agent);
            }
            else
            {
                agent = queue.MessageAgent;
            }

            agent.SendMessage(Connection, msg, cons.TimeLimit, null);
            return myqueue;
        }

        /*
        * Ldap URL search
        */

        /// <summary>
        ///     Synchronously performs the search specified by the Ldap URL, returning
        ///     an enumerable LdapSearchResults object.
        /// </summary>
        /// <param name="toGet">
        ///     The Ldap URL specifying the entry to read.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public static ILdapSearchResults Search(LdapUrl toGet)
        {
            // Get a clone of default search constraints, method alters batchSize
            return Search(toGet, null);
        }

        /*
        * Ldap URL search
        */

        /// <summary>
        ///     Synchronously perfoms the search specified by the Ldap URL, using
        ///     the specified search constraints (such as the maximum number of
        ///     entries to find or the maximum time to wait for search results).
        ///     When this method is called, a new connection is created
        ///     automatically, using the host and port specified in the URL. After
        ///     all search results have been received from the server, the method
        ///     closes the connection (in other words, it disconnects from the Ldap
        ///     server).
        ///     As part of the search constraints, a choice can be made as to whether
        ///     to have the results delivered all at once or in smaller batches. If
        ///     the results are to be delivered in smaller batches, each iteration
        ///     blocks only until the next batch of results is returned.
        /// </summary>
        /// <param name="toGet">
        ///     Ldap URL specifying the entry to read.
        /// </param>
        /// <param name="cons">
        ///     The constraints specific to the search.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        public static ILdapSearchResults Search(LdapUrl toGet, LdapSearchConstraints cons)
        {
            using (var lconn = new LdapConnection())
            {
                lconn.Connect(toGet.Host, toGet.Port);
                if (cons == null)
                {
                    // This is a clone, so we already have our own copy
                    cons = lconn.SearchConstraints;
                }
                else
                {
                    // get our own copy of user's constraints because we modify it
                    cons = (LdapSearchConstraints)cons.Clone();
                }

                cons.BatchSize = 0; // Must wait until all results arrive
                var toReturn = lconn.Search(toGet.GetDn(), toGet.Scope, toGet.Filter, toGet.AttributeArray, false,
                    cons);
                return toReturn;
            }
        }

        /// <summary>
        ///     Sends an Ldap request to a directory server.
        ///     The specified the Ldap request is sent to the directory server
        ///     associated with this connection using default constraints. An Ldap
        ///     request object is a subclass {@link LdapMessage} with the operation
        ///     type set to one of the request types. You can build a request by using
        ///     the request classes found in this package
        ///     You should note that, since Ldap requests sent to the server
        ///     using sendRequest are asynchronous, automatic referral following
        ///     does not apply to these requests.
        /// </summary>
        /// <param name="request">
        ///     The Ldap request to send to the directory server.
        /// </param>
        /// <param name="queue">
        ///     The queue for messages returned from a server in
        ///     response to this request. If it is null, a
        ///     queue object is created internally.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        /// <seealso cref="LdapMessage.Type">
        /// </seealso>
        /// <seealso cref="RfcLdapMessage.IsRequest">
        /// </seealso>
        public LdapMessageQueue SendRequest(LdapMessage request, LdapMessageQueue queue)
        {
            return SendRequest(request, queue, null);
        }

        /// <summary>
        ///     Sends an Ldap request to a directory server.
        ///     The specified the Ldap request is sent to the directory server
        ///     associated with this connection. An Ldap request object is an
        ///     {@link LdapMessage} with the operation type set to one of the request
        ///     types. You can build a request by using the request classes found in this
        ///     package
        ///     You should note that, since Ldap requests sent to the server
        ///     using sendRequest are asynchronous, automatic referral following
        ///     does not apply to these requests.
        /// </summary>
        /// <param name="request">
        ///     The Ldap request to send to the directory server.
        /// </param>
        /// <param name="queue">
        ///     The queue for messages returned from a server in
        ///     response to this request. If it is null, a
        ///     queue object is created internally.
        /// </param>
        /// <param name="cons">
        ///     The constraints that apply to this request.
        /// </param>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        /// <seealso cref="LdapMessage.Type">
        /// </seealso>
        /// <seealso cref="RfcLdapMessage.IsRequest">
        /// </seealso>
        public LdapMessageQueue SendRequest(LdapMessage request, LdapMessageQueue queue, LdapConstraints cons)
        {
            if (!request.Request)
            {
                throw new Exception("Object is not a request message");
            }

            if (cons == null)
            {
                cons = _defSearchCons;
            }

            // Get the correct queue for a search request
            MessageAgent agent;
            var myqueue = queue;
            if (myqueue == null)
            {
                agent = new MessageAgent();
                if (request.Type == LdapMessage.SearchRequest)
                {
                    myqueue = new LdapSearchQueue(agent);
                }
                else
                {
                    myqueue = new LdapResponseQueue(agent);
                }
            }
            else
            {
                if (request.Type == LdapMessage.SearchRequest)
                {
                    agent = queue.MessageAgent;
                }
                else
                {
                    agent = queue.MessageAgent;
                }
            }

            agent.SendMessage(Connection, request, cons.TimeLimit, null);

            return myqueue;
        }

        // *************************************************************************
        // helper methods
        // *************************************************************************

        /// <summary>
        ///     Locates the appropriate message agent and sends
        ///     the Ldap request to a directory server.
        /// </summary>
        /// <param name="msg">
        ///     the message to send.
        /// </param>
        /// <param name="timeout">
        ///     the timeout value.
        /// </param>
        /// <param name="queue">
        ///     the response queue or null.
        /// </param>
        /// <returns>
        ///     the LdapResponseQueue for this request.
        /// </returns>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        private LdapResponseQueue SendRequestToServer(LdapMessage msg, int timeout, LdapResponseQueue queue,
            BindProperties bindProps)
        {
            MessageAgent agent;
            if (queue == null)
            {
                agent = new MessageAgent();
                queue = new LdapResponseQueue(agent);
            }
            else
            {
                agent = queue.MessageAgent;
            }

            agent.SendMessage(Connection, msg, timeout, bindProps);
            return queue;
        }

        /// <summary>
        ///     get an LdapConnection object so that we can follow a referral.
        ///     This function is never called if cons.getReferralFollowing() returns
        ///     false.
        /// </summary>
        /// <param name="referrals">
        ///     the array of referral strings.
        /// </param>
        /// <returns>
        ///     The referralInfo object.
        /// </returns>
        /// <exception>
        ///     LdapReferralException A general exception which includes
        ///     an error message and an Ldap error code.
        /// </exception>
        private ReferralInfo GetReferralConnection(string[] referrals)
        {
            ReferralInfo refInfo = null;
            Exception ex = null;
            LdapConnection rconn = null;
            var rh = _defSearchCons.getReferralHandler();

            // Check if we use LdapRebind to get authentication credentials
            if (rh == null || rh is ILdapAuthHandler)
            {
                int i;
                for (i = 0; i < referrals.Length; i++)
                {
                    // dn, pw are null in the default case (anonymous bind)
                    string dn = null;
                    byte[] pw = null;
                    try
                    {
                        rconn = new LdapConnection
                        {
                            Constraints = _defSearchCons
                        };
                        var url = new LdapUrl(referrals[i]);
                        rconn.Connect(url.Host, url.Port);
                        if (rh is ILdapAuthHandler)
                        {
                            // Get application supplied dn and pw
                            var ap = ((ILdapAuthHandler)rh).GetAuthProvider(url.Host, url.Port);
                            dn = ap.Dn;
                            pw = ap.Password;
                        }

                        rconn.Bind(LdapV3, dn, pw);
                        ex = null;
                        refInfo = new ReferralInfo(rconn, referrals, url);

                        // Indicate this connection created to follow referral
                        rconn.Connection.ActiveReferral = refInfo;
                        break;
                    }
                    catch (Exception lex)
                    {
                        if (rconn != null)
                        {
                            try
                            {
                                rconn.Disconnect();
                                rconn = null;
                                ex = lex;
                            }
                            catch (LdapException ldapException)
                            {
                                Logger.Warn("Exception swallowed", ldapException);
                            }
                        }
                    }
                }
            }

            // Check if application gets connection and does bind
            else
            {
                // rh instanceof LdapBind
                try
                {
                    rconn = ((ILdapBindHandler)rh).Bind(referrals, this);
                    if (rconn == null)
                    {
                        var rex = new LdapReferralException(ExceptionMessages.ReferralError);
                        rex.SetReferrals(referrals);
                        throw rex;
                    }

                    // Figure out which Url belongs to the connection
                    for (var idx = 0; idx < referrals.Length; idx++)
                    {
                        try
                        {
                            var url = new LdapUrl(referrals[idx]);
                            if (url.Host.EqualsOrdinalCI(rconn.Host) && url.Port == rconn.Port)
                            {
                                refInfo = new ReferralInfo(rconn, referrals, url);
                                break;
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Warn("Exception swallowed", e);
                        }
                    }

                    if (refInfo == null)
                    {
                        // Could not match LdapBind.bind() connecction with URL list
                        ex = new LdapLocalException(ExceptionMessages.ReferralBindMatch, LdapException.ConnectError);
                    }
                }
                catch (Exception lex)
                {
                    rconn = null;
                    ex = lex;
                }
            }

            if (ex != null)
            {
                // Could not connect to any server, throw an exception
                LdapException ldapex;
                if (ex is LdapReferralException)
                {
                    throw (LdapReferralException)ex;
                }

                if (ex is LdapException)
                {
                    ldapex = (LdapException)ex;
                }
                else
                {
                    ldapex = new LdapLocalException(
                        ExceptionMessages.ServerConnectError,
                        new object[] {Connection.Host },
                        LdapException.ConnectError, ex);
                }

                // Error attempting to follow a referral
                var rex = new LdapReferralException(ExceptionMessages.ReferralError, ldapex);
                rex.SetReferrals(referrals);

                // Use last URL string for the failed referral
                rex.FailedReferral = referrals[referrals.Length - 1];
                throw rex;
            }

            // We now have an authenticated connection
            // to be used to follow the referral.
            return refInfo;
        }

        /// <summary>
        ///     Check the result code and throw an exception if needed.
        ///     If referral following is enabled, checks if we need to
        ///     follow a referral.
        /// </summary>
        /// <param name="queue">
        ///     - the message queue of the current response.
        /// </param>
        /// <param name="cons">
        ///     - the constraints that apply to the request.
        /// </param>
        /// <param name="response">
        ///     - the LdapResponse to check.
        /// </param>
        private void ChkResultCode(LdapMessageQueue queue, LdapConstraints cons, LdapResponse response)
        {
            if (response.ResultCode == LdapException.Referral && cons.ReferralFollowing)
            {
                // BUG: refConn is not used, and thus ReleaseReferralConnections won't do anything?
                // Pretty sure that the last argument to ChaseReferral should be refConn instead of null

                // Perform referral following and return
                ArrayList refConn = null;
                try
                {
                    ChaseReferral(queue, cons, response, response.Referrals, 0, false, null);
                }
                finally
                {
                    ReleaseReferralConnections(refConn);
                }
            }
            else
            {
                // Throws exception for non success result
                response.ChkResultCode();
            }
        }

        /// <summary>
        ///     Follow referrals if necessary referral following enabled.
        ///     This function is called only by synchronous requests.
        ///     Search responses come here only if referral following is
        ///     enabled and if we are processing a SearchResultReference
        ///     or a Response with a status of REFERRAL, i.e. we are
        ///     going to follow a referral.
        ///     This functions recursively follows a referral until a result
        ///     is returned or until the hop limit is reached.
        /// </summary>
        /// <param name="queue">
        ///     The LdapResponseQueue for this request.
        /// </param>
        /// <param name="cons">
        ///     The constraints that apply to the request.
        /// </param>
        /// <param name="msg">
        ///     The referral or search reference response message.
        /// </param>
        /// <param name="initialReferrals">
        ///     The referral array returned from the
        ///     initial request.
        /// </param>
        /// <param name="hopCount">
        ///     the number of hops already used while
        ///     following this referral.
        /// </param>
        /// <param name="searchReference">
        ///     true if the message is a search reference.
        /// </param>
        /// <param name="connectionList">
        ///     An optional array list used to store
        ///     the LdapConnection objects used in following the referral.
        /// </param>
        /// <returns>
        ///     The array list used to store the all LdapConnection objects
        ///     used in following the referral.  The list will be empty
        ///     if there were none.
        /// </returns>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        internal ArrayList ChaseReferral(LdapMessageQueue queue, LdapConstraints cons, LdapMessage msg,
            string[] initialReferrals, int hopCount, bool searchReference, ArrayList connectionList)
        {
            var connList = connectionList;
            ReferralInfo rinfo = null; // referral info
            LdapMessage origMsg;

            // Get a place to store new connections
            if (connList == null)
            {
                connList = new ArrayList(cons.HopLimit);
            }

            // Following referrals or search reference
            string[] refs; // referral list
            if (initialReferrals != null)
            {
                // Search continuation reference from a search request
                refs = initialReferrals;
                origMsg = msg.RequestingMessage;
            }
            else
            {
                // Not a search request
                var resp = (LdapResponse)queue.GetResponse();
                if (resp.ResultCode != LdapException.Referral)
                {
                    // Not referral result,throw Exception if nonzero result
                    resp.ChkResultCode();
                    return connList;
                }

                // We have a referral response
                refs = resp.Referrals;
                origMsg = resp.RequestingMessage;
            }

            LdapUrl refUrl; // referral represented as URL
            try
            {
                // increment hop count, check max hops
                if (hopCount++ > cons.HopLimit)
                {
                    throw new LdapLocalException("Max hops exceeded", LdapException.ReferralLimitExceeded);
                }

                // Get a connection to follow the referral
                rinfo = GetReferralConnection(refs);
                var rconn = rinfo.ReferralConnection; // new conn for following referral
                refUrl = rinfo.ReferralUrl;
                connList.Add(rconn);

                // rebuild msg into new msg changing msgID,dn,scope,filter
                var newMsg = RebuildRequest(origMsg, refUrl, searchReference);

                // Send new message on new connection
                try
                {
                    MessageAgent agent;
                    if (queue is LdapResponseQueue)
                    {
                        agent = queue.MessageAgent;
                    }
                    else
                    {
                        agent = queue.MessageAgent;
                    }

                    agent.SendMessage(rconn.Connection, newMsg, _defSearchCons.TimeLimit, null);
                }
                catch (InterThreadException ex)
                {
                    // Error ending request to referred server
                    var rex = new LdapReferralException(ExceptionMessages.ReferralSend, LdapException.ConnectError,
                        null, ex);
                    rex.SetReferrals(initialReferrals);
                    var refRenamed = rconn.Connection.ActiveReferral;
                    rex.FailedReferral = refRenamed.ReferralUrl.ToString();
                    throw rex;
                }

                if (initialReferrals == null)
                {
                    // For operation results, when all responses are complete,
                    // the stack unwinds back to the original and returns
                    // to the application.
                    // An exception is thrown for an error
                    connList = ChaseReferral(queue, cons, null, null, hopCount, false, connList);
                }
                else
                {
                    // For search, just return to LdapSearchResults object
                    return connList;
                }
            }
            catch (Exception ex)
            {
                if (ex is LdapReferralException)
                {
                    throw (LdapReferralException)ex;
                }

                // Set referral list and failed referral
                var rex = new LdapReferralException(ExceptionMessages.ReferralError, ex);
                rex.SetReferrals(refs);
                if (rinfo != null)
                {
                    rex.FailedReferral = rinfo.ReferralUrl.ToString();
                }
                else
                {
                    rex.FailedReferral = refs[refs.Length - 1];
                }

                throw rex;
            }

            return connList;
        }

        /// <summary>
        ///     Builds a new request replacing dn, scope, and filter where approprate.
        /// </summary>
        /// <param name="msg">
        ///     the original LdapMessage to build the new request from.
        /// </param>
        /// <param name="url">
        ///     the referral url.
        /// </param>
        /// <returns>
        ///     a new LdapMessage with appropriate information replaced.
        /// </returns>
        /// <exception>
        ///     LdapException A general exception which includes an error
        ///     message and an Ldap error code.
        /// </exception>
        private LdapMessage RebuildRequest(LdapMessage msg, LdapUrl url, bool reference)
        {
            var dn = url.GetDn(); // new base
            string filter = null;

            switch (msg.Type)
            {
                case LdapMessage.SearchRequest:
                    if (reference)
                    {
                        filter = url.Filter;
                    }

                    break;

                // We are allowed to get a referral for the following

                case LdapMessage.AddRequest:
                case LdapMessage.BindRequest:
                case LdapMessage.CompareRequest:
                case LdapMessage.DelRequest:
                case LdapMessage.ExtendedRequest:
                case LdapMessage.ModifyRdnRequest:
                case LdapMessage.ModifyRequest:
                    break;
                default:
                    throw new LdapLocalException(ExceptionMessages.ImproperReferral, new object[] {msg.Type },
                        LdapException.LocalError);
            }

            return msg.Clone(dn, filter, reference);
        }

        /*
        * Release connections acquired by following referrals
        *
        * @param list the list of the connections
        */

        internal void ReleaseReferralConnections(ArrayList list)
        {
            if (list == null)
            {
                return;
            }

            // Release referral connections
            for (var i = list.Count - 1; i >= 0; i--)
            {
                try
                {
                    var rconn = (LdapConnection)list[i];
                    list.RemoveAt(i);
                    rconn.Disconnect();
                }
                catch (IndexOutOfRangeException ex)
                {
                    Logger.Warn("Exception swallowed", ex);
                }
                catch (LdapException lex)
                {
                    Logger.Warn("Exception swallowed", lex);
                }
            }
        }

        // *************************************************************************
        // Schema Related methods
        // *************************************************************************

        /// <summary>
        ///     Retrieves the schema associated with a particular schema DN in the
        ///     directory server.
        ///     The schema DN for a particular entry is obtained by calling the
        ///     getSchemaDN method of LDAPConnection.
        /// </summary>
        /// <param name="schemaDn">
        ///     The schema DN used to fetch the schema.
        /// </param>
        /// <returns>
        ///     An LDAPSchema entry containing schema attributes.  If the
        ///     entry contains no schema attributes then the returned LDAPSchema object
        ///     will be empty.
        /// </returns>
        /// <exception>
        ///     LDAPException     This exception occurs if the schema entry
        ///     cannot be retrieved with this connection.
        /// </exception>
        /// <seealso cref="GetSchemaDn">
        /// </seealso>
        /// <seealso cref="GetSchemaDn(string)">
        /// </seealso>
        public LdapSchema FetchSchema(string schemaDn)
        {
            var ent = Read(schemaDn, LdapSchema.SchemaTypeNames);
            return new LdapSchema(ent);
        }

        /// <summary>
        ///     Retrieves the Distiguished Name (DN) for the schema advertised in the
        ///     root DSE of the Directory Server.
        ///     The DN can be used with the methods fetchSchema and modify to retreive
        ///     and extend schema definitions.  The schema entry is located by reading
        ///     subschemaSubentry attribute of the root DSE.  This is equivalent to
        ///     calling {@link #getSchemaDN(String) } with the DN parameter as an empty
        ///     string: <code>getSchemaDN("")</code>.
        /// </summary>
        /// <returns>
        ///     Distinguished Name of a schema entry in effect for the
        ///     Directory.
        /// </returns>
        /// <exception>
        ///     LDAPException     This exception occurs if the schema DN
        ///     cannot be retrieved, or if the subschemaSubentry attribute associated
        ///     with the root DSE contains multiple values.
        /// </exception>
        /// <seealso cref="FetchSchema">
        /// </seealso>
        /// <seealso cref="Modify">
        /// </seealso>
        public string GetSchemaDn()
        {
            return GetSchemaDn(string.Empty);
        }

        /// <summary>
        ///     Retrieves the Distiguished Name (DN) of the schema associated with a
        ///     entry in the Directory.
        ///     The DN can be used with the methods fetchSchema and modify to retreive
        ///     and extend schema definitions.  Reads the subschemaSubentry of the entry
        ///     specified.
        /// </summary>
        /// <param name="dn">
        ///     Distinguished Name of any entry.  The subschemaSubentry
        ///     attribute is queried from this entry.
        /// </param>
        /// <returns>
        ///     Distinguished Name of a schema entry in effect for the entry
        ///     identified by. <code>dn</code>.
        /// </returns>
        /// <exception>
        ///     LDAPException     This exception occurs if a null or empty
        ///     value is passed as dn, if the subschemasubentry attribute cannot
        ///     be retrieved, or the subschemasubentry contains multiple values.
        /// </exception>
        /// <seealso cref="FetchSchema">
        /// </seealso>
        /// <seealso cref="Modify">
        /// </seealso>
        public string GetSchemaDn(string dn)
        {
            string[] attrSubSchema = {"subschemaSubentry" };

            /* Read the entries subschemaSubentry attribute. Throws an exception if
            * no entries are returned. */
            var ent = Read(dn, attrSubSchema);

            var attr = ent.GetAttribute(attrSubSchema[0]);
            var values = attr.StringValueArray;
            if (values == null || values.Length < 1)
            {
                throw new LdapLocalException(ExceptionMessages.NoSchema, new object[] {dn },
                    LdapException.NoResultsReturned);
            }

            if (values.Length > 1)
            {
                throw new LdapLocalException(ExceptionMessages.MultipleSchema, new object[] {dn },
                    LdapException.ConstraintViolation);
            }

            return values[0];
        }
    }
}