mergeInto(LibraryManager.library, {
    ConnectWebSocket: function (urlPtr) {
        var url = UTF8ToString(urlPtr);

        // SessionFlowController can receive the host session event at the same time as its
        // legacy timeout completes. Do not replace a live socket with a new CONNECTING socket:
        // commands sent during that small window used to throw InvalidStateError and abort the
        // entire Unity WebGL player.
        var currentSocket = window.UnityWebSocket;
        if (currentSocket &&
            (currentSocket.readyState === WebSocket.CONNECTING ||
             currentSocket.readyState === WebSocket.OPEN)) {
            console.warn("WebSocket connection request ignored because a socket is already active");
            return;
        }

        var socket = new WebSocket(url);

        window.UnityWebSocket = socket;
        window.UnityWebSocketPendingMessages = window.UnityWebSocketPendingMessages || [];

        socket.onopen = function () {
            if (window.UnityWebSocket !== socket) {
                return;
            }

            console.log("WebSocket connected to " + url);

            var pendingMessages = window.UnityWebSocketPendingMessages || [];
            window.UnityWebSocketPendingMessages = [];
            for (var i = 0; i < pendingMessages.length; i++) {
                try {
                    socket.send(pendingMessages[i]);
                } catch (error) {
                    console.error("Failed to flush queued WebSocket message: ", error);
                    window.UnityWebSocketPendingMessages = pendingMessages.slice(i);
                    break;
                }
            }

            if (typeof unityInstance !== "undefined") {
                unityInstance.SendMessage('WebSocketLibraryWrapper', 'OnWebSocketOpen');
            } else {
                console.error("unityInstance is not defined");
            }
        };

        socket.onmessage = function (event) {
            if (window.UnityWebSocket !== socket) {
                return;
            }

            console.log("Message received: " + event.data);
            if (typeof unityInstance !== "undefined") {
                unityInstance.SendMessage('WebSocketLibraryWrapper', 'OnWebSocketMessage', event.data);
            } else {
                console.error("unityInstance is not defined");
            }
        };

        socket.onclose = function (event) {
            if (window.UnityWebSocket !== socket) {
                return;
            }

            console.log("WebSocket connection closed, code and reason:", event.code, event.reason);
            if (typeof unityInstance !== "undefined") {
                unityInstance.SendMessage('WebSocketLibraryWrapper', 'OnWebSocketClose', event.code);
            } else {
                console.error("unityInstance is not defined");
            }
            window.UnityWebSocket = null;
        };

        socket.onerror = function (error) {
            if (window.UnityWebSocket !== socket) {
                return;
            }

            console.error("WebSocket connection error: ", error);
            if (typeof unityInstance !== "undefined") {
                unityInstance.SendMessage('WebSocketLibraryWrapper', 'OnWebSocketError', error.type);
            } else {
                console.error("unityInstance is not defined");
            }
        };
    },

    SendWebSocketMessage: function (messagePtr) {
        var message = UTF8ToString(messagePtr);
        var socket = window.UnityWebSocket;

        if (socket && socket.readyState === WebSocket.OPEN) {
            console.log("Sending message: " + message);
            try {
                socket.send(message);
            } catch (error) {
                // Never allow a browser networking race to escape into Unity's main loop.
                console.error("Failed to send WebSocket message: ", error);
            }
        } else if (socket && socket.readyState === WebSocket.CONNECTING) {
            var pendingMessages = window.UnityWebSocketPendingMessages || [];
            // Bound the queue so a broken host cannot grow memory forever.
            if (pendingMessages.length < 100) {
                pendingMessages.push(message);
                window.UnityWebSocketPendingMessages = pendingMessages;
                console.log("WebSocket is connecting; message queued");
            } else {
                console.error("WebSocket pending-message queue is full");
            }
        } else {
            console.warn("WebSocket is not open; message was not sent");
        }
    },

    CloseWebSocket: function () {
        var socket = window.UnityWebSocket;
        window.UnityWebSocketPendingMessages = [];

        if (socket &&
            (socket.readyState === WebSocket.CONNECTING || socket.readyState === WebSocket.OPEN)) {
            console.log("Closing WebSocket connection...");
            socket.close();
        } else {
            console.error("No active WebSocket connection to close");
        }
    },

    NavigateToHome: function () {
        var currentUrl = window.location.origin;
        var homeUrl = currentUrl + "/home";
        console.log("Navigating to: " + homeUrl);
        window.location.href = homeUrl;
    },

    SendSessionEndMessage: function () {
        console.log("Sending session_end postMessage to parent window");
        window.parent.postMessage({
            type: "session_end"
        }, "*");
    },
	
	SendSessionOptionsMessageWithAction: function (actionPtr) {
        var action = UTF8ToString(actionPtr);
        if (!action) {
            console.warn("SendSessionOptionsMessageWithAction: empty action");
            return;
        }
        console.log("Sending session_options with action:", action);
        window.parent.postMessage({ type: "session_options", action: action }, "*");
    }
});
