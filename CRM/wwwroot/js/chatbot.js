// Chatbot JavaScript
document.addEventListener('DOMContentLoaded', function() {
    const sessionId = document.querySelector('#chatbotWidget')?.dataset?.sessionId || generateSessionId();
    let chatOpen = false;
    let messageCount = 0;
    let signalRConnection = null;

    // DOM Elements
    const chatButton = document.getElementById('chatButton');
    const chatWindow = document.getElementById('chatWindow');
    const chatMessages = document.getElementById('chatMessages');
    const chatInput = document.getElementById('chatInput');
    const chatSend = document.getElementById('chatSend');
    const chatClose = document.getElementById('chatClose');
    const chatMinimize = document.getElementById('chatMinimize');
    const chatBadge = document.getElementById('chatBadge');
    const typingIndicator = document.getElementById('typingIndicator');
    const leadCaptureForm = document.getElementById('leadCaptureForm');
    const chatInputContainer = document.getElementById('chatInputContainer');
    const leadForm = document.getElementById('leadForm');
    const cancelLeadForm = document.getElementById('cancelLeadForm');

    // Initialize chat first
    initializeChat();
    
    // Initialize SignalR connection (will retry if not loaded yet)
    initializeSignalR();
    
    // Initialize SignalR connection with retry
    function initializeSignalR() {
        // Check if SignalR library is loaded
        if (typeof signalR === 'undefined') {
            console.warn('SignalR not loaded yet, retrying in 1 second...');
            setTimeout(initializeSignalR, 1000);
            return;
        }
        
        try {
            signalRConnection = new signalR.HubConnectionBuilder()
                .withUrl("/chatHub")
                .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
                .configureLogging(signalR.LogLevel.Information)
                .build();

            // Handle incoming agent messages
            signalRConnection.on("ReceiveAgentMessage", function (data) {
                console.log("Received agent message via SignalR:", data);
                // Add agent message to chat
                addMessage(data.message, 'Agent', new Date(data.timestamp));
                // Show notification if chat is closed
                if (!chatOpen) {
                    showNotification("New message from agent");
                }
            });

            // Join session group
            signalRConnection.start()
                .then(function () {
                    console.log("SignalR Connected! SessionId:", sessionId);
                    return signalRConnection.invoke("JoinSession", sessionId);
                })
                .then(function () {
                    console.log("Joined session group:", sessionId);
                })
                .catch(function (err) {
                    console.error("SignalR Error:", err.toString());
                });

            // Handle reconnection
            signalRConnection.onreconnecting(function (error) {
                console.warn("SignalR reconnecting...", error);
            });
            
            signalRConnection.onreconnected(function (connectionId) {
                console.log("SignalR reconnected!");
                signalRConnection.invoke("JoinSession", sessionId)
                    .catch(function (err) {
                        console.error("Error rejoining session:", err.toString());
                    });
            });

        } catch (err) {
            console.error("Failed to setup SignalR:", err);
        }
    }

    // Event Listeners
    if (chatButton) {
        chatButton.addEventListener('click', toggleChat);
    }
    if (chatClose) {
        chatClose.addEventListener('click', closeChat);
    }
    if (chatMinimize) {
        chatMinimize.addEventListener('click', minimizeChat);
    }
    if (chatSend) {
        chatSend.addEventListener('click', sendMessage);
    }
    if (chatInput) {
        chatInput.addEventListener('keypress', function(e) {
            if (e.key === 'Enter') {
                sendMessage();
            }
        });
    }
    if (leadForm) {
        leadForm.addEventListener('submit', function(e) {
            e.preventDefault();
            submitLeadForm();
        });
    }
    if (cancelLeadForm) {
        cancelLeadForm.addEventListener('click', function() {
            leadCaptureForm.style.display = 'none';
            chatInputContainer.style.display = 'block';
        });
    }

    function generateSessionId() {
        return 'session_' + Math.random().toString(36).substr(2, 9) + '_' + Date.now();
    }

    function initializeChat() {
        console.log('Initializing chatbot...');
        loadConversation();
    }

    function toggleChat() {
        console.log('Chat button clicked! Current state:', chatOpen);
        chatOpen = !chatOpen;
        console.log('New state:', chatOpen);
        if (chatOpen) {
            openChat();
        } else {
            closeChat();
        }
    }

    function openChat() {
        console.log('Opening chat window...');
        if (chatWindow) {
            chatWindow.classList.add('open');
            // Force display with inline styles
            chatWindow.style.display = 'flex';
            chatWindow.style.visibility = 'visible';
            chatWindow.style.opacity = '1';
            console.log('Chat window display set to flex');
        }
        if (chatButton) {
            chatButton.style.display = 'none';
            console.log('Chat button hidden');
        }
        if (chatBadge) {
            chatBadge.style.display = 'none';
        }
        messageCount = 0;
        console.log('Chat opened successfully');
    }

    function closeChat() {
        console.log('Closing chat window...');
        if (chatWindow) {
            chatWindow.classList.remove('open');
            // Force hide with inline styles
            chatWindow.style.display = 'none';
            chatWindow.style.visibility = 'hidden';
            chatWindow.style.opacity = '0';
            console.log('Chat window hidden');
        }
        if (chatButton) {
            chatButton.style.display = 'flex';
            console.log('Chat button shown');
        }
        chatOpen = false;
        console.log('Chat closed successfully');
    }

    function minimizeChat() {
        if (chatWindow) {
            chatWindow.classList.remove('open');
        }
        if (chatButton) {
            chatButton.style.display = 'flex';
        }
        chatOpen = false;
    }

    async function loadConversation() {
        try {
            const response = await fetch(`${window.location.origin}/api/chatbot/getconversation?sessionId=` + encodeURIComponent(sessionId));
            
            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }
            
            const text = await response.text();
            let data;
            
            try {
                data = JSON.parse(text);
            } catch (parseError) {
                console.error("Invalid JSON response:", text);
                throw new Error("Invalid JSON response from server");
            }
            
            if (data.success && chatMessages) {
                data.messages.forEach(msg => {
                    // Handle both old and new message formats for compatibility
                    const messageText = msg.messageText || msg.UserMessage || 'No message';
                    const senderType = msg.senderType || (msg.UserMessage ? 'User' : 'Bot');
                    const timestamp = msg.sentAt || msg.CreatedOn || new Date();
                    
                    addMessage(messageText, senderType, new Date(timestamp));
                });
                
                if (data.messages.length === 0) {
                    // Show welcome message
                    addMessage("Hello! Welcome to our Real Estate CRM. I'm here to help you with property information, pricing details, and any questions you might have. How can I assist you today?", "Bot", new Date());
                }
            }
        } catch (error) {
            console.error('Error loading conversation:', error);
            // Show welcome message even if there's an error
            if (chatMessages) {
                addMessage("Hello! Welcome to our Real Estate CRM. I'm here to help you with property information, pricing details, and any questions you might have. How can I assist you today?", "Bot", new Date());
            }
        }
    }

    async function sendMessage() {
        if (!chatInput) return;
        
        const message = chatInput.value.trim();
        const currentImage = window.getCurrentChatImage?.();
        
        if (!message && !currentImage) return;

        // Show typing indicator
        showTypingIndicator();

        try {
            let response;
            
            if (currentImage) {
                // Handle image upload
                const formData = new FormData();
                formData.append('image', dataURLtoFile(currentImage, 'chat-image.png'));
                formData.append('sessionId', sessionId);
                formData.append('message', message || '');

                // Add image message to chat
                if (window.addImageMessage) {
                    window.addImageMessage(currentImage, 'User', new Date());
                }

                response = await fetch(`${window.location.origin}/api/chatbot/uploadimage`, {
                    method: 'POST',
                    body: formData
                });

                // Clear image after sending
                window.clearCurrentChatImage?.();
            } else {
                // Handle text message
                addMessage(message, 'User', new Date());

                response = await fetch(`${window.location.origin}/api/chatbot/sendmessage`, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                    },
                    body: JSON.stringify({
                        sessionId: sessionId,
                        message: message
                    })
                });
            }

            chatInput.value = '';
            if (chatSend) {
                chatSend.disabled = true;
            }

            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }

            const text = await response.text();
            let data;
            
            try {
                data = JSON.parse(text);
            } catch (parseError) {
                console.error("Invalid JSON response:", text);
                throw new Error("Invalid JSON response from server");
            }

            if (data.success) {
                hideTypingIndicator();
                
                // Handle enhanced chatbot response format
                const botResponse = data.response || data.message?.messageText || 'Okay, I am here to help.';
                const botTimestamp = data.timestamp || new Date().toISOString();
                
                addMessage(botResponse, 'Bot', new Date(botTimestamp));
                
                // Lead capture is handled automatically by enhanced chatbot service - no separate form needed
                // The bot will collect information during the chat flow
                
                // Check if agent transfer is needed
                if (data.shouldTransferToAgent && data.assignedAgentName) {
                    addMessage(`${data.assignedAgentName} has been assigned to help you. They will contact you shortly.`, 'Bot', new Date());
                }
            } else {
                hideTypingIndicator();
                addMessage(data.error || 'Sorry, I encountered an error. Please try again.', 'Bot', new Date());
            }
        } catch (error) {
            hideTypingIndicator();
            console.error('Error sending message:', error);
            
            let errorMessage = 'Sorry, I encountered an error. Please try again.';
            if (error.message.includes('500')) {
                errorMessage = 'Server error occurred. Please try again in a moment.';
            } else if (error.message.includes('JSON')) {
                errorMessage = 'Communication error. Please refresh the page and try again.';
            }
            
            addMessage(errorMessage, 'Bot', new Date());
        } finally {
            if (chatSend) {
                chatSend.disabled = false;
            }
            if (chatInput) {
                chatInput.focus();
            }
        }
    }

    function addMessage(text, sender, timestamp) {
        if (!chatMessages) return;
        
        const messageDiv = document.createElement('div');
        messageDiv.className = `message ${sender.toLowerCase()}`;
        
        const avatarDiv = document.createElement('div');
        avatarDiv.className = 'message-avatar';
        avatarDiv.textContent = sender === 'Bot' ? 'AI' : 'U';
        
        const contentDiv = document.createElement('div');
        contentDiv.className = 'message-content';
        
        const bubbleDiv = document.createElement('div');
        bubbleDiv.className = 'message-bubble';
        
        // Handle text properly - preserve line breaks and spaces
        if (typeof text === 'string') {
            // Replace newlines with HTML line breaks for better display
            const formattedText = text.replace(/\n/g, '<br>').replace(/  /g, ' &nbsp;');
            bubbleDiv.innerHTML = formattedText;
        } else {
            bubbleDiv.textContent = String(text);
        }
        
        const timeDiv = document.createElement('div');
        timeDiv.className = 'message-time';
        timeDiv.textContent = formatTime(timestamp);
        
        contentDiv.appendChild(bubbleDiv);
        contentDiv.appendChild(timeDiv);
        
        messageDiv.appendChild(avatarDiv);
        messageDiv.appendChild(contentDiv);
        
        chatMessages.appendChild(messageDiv);
        
        // Scroll to bottom smoothly
        setTimeout(() => {
            if (chatMessages) {
                chatMessages.scrollTop = chatMessages.scrollHeight;
            }
        }, 100);
    }

    function showTypingIndicator() {
        if (typingIndicator) {
            typingIndicator.style.display = 'flex';
            if (chatMessages) {
                chatMessages.scrollTop = chatMessages.scrollHeight;
            }
        }
    }

    function hideTypingIndicator() {
        if (typingIndicator) {
            typingIndicator.style.display = 'none';
        }
    }

    function showLeadCaptureForm() {
        if (leadCaptureForm) {
            leadCaptureForm.style.display = 'block';
        }
        if (chatInputContainer) {
            chatInputContainer.style.display = 'none';
        }
    }

    async function submitLeadForm() {
        const name = document.getElementById('leadName')?.value.trim();
        const phone = document.getElementById('leadPhone')?.value.trim();
        const email = document.getElementById('leadEmail')?.value.trim();

        if (!name || !phone) {
            showToast('Please provide your name and phone number', 'warning');
            return;
        }

        try {
            // Send the lead information as a chat message - the enhanced chatbot service will handle lead creation
            const leadMessage = `My details: Name: ${name}, Phone: ${phone}${email ? ', Email: ' + email : ''}`;
            
            const response = await fetch(`${window.location.origin}/api/chatbot/sendmessage`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({
                    sessionId: sessionId,
                    message: leadMessage
                })
            });

            const data = await response.json();
            
            if (data.success) {
                addMessage("Thank you! I've captured your information. Our team will contact you soon with personalized property recommendations.", 'Bot', new Date());
                if (leadCaptureForm) {
                    leadCaptureForm.style.display = 'none';
                }
                if (chatInputContainer) {
                    chatInputContainer.style.display = 'block';
                }
                
                // Reset form
                const nameField = document.getElementById('leadName');
                const phoneField = document.getElementById('leadPhone');
                const emailField = document.getElementById('leadEmail');
                if (nameField) nameField.value = '';
                if (phoneField) phoneField.value = '';
                if (emailField) emailField.value = '';
            } else {
                addMessage('Sorry, there was an error capturing your information. Please try again.', 'Bot', new Date());
            }
        } catch (error) {
            addMessage('Sorry, there was an error capturing your information. Please try again.', 'Bot', new Date());
            console.error('Error capturing lead:', error);
        }
    }

    function formatTime(date) {
        return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    }

    function dataURLtoFile(dataurl, filename) {
        const arr = dataurl.split(',');
        const mime = arr[0].match(/:(.*?);/)[1];
        const bstr = atob(arr[1]);
        let n = bstr.length;
        const u8arr = new Uint8Array(n);
        
        while(n--){
            u8arr[n] = bstr.charCodeAt(n);
        }
        
        return new File([u8arr], filename, {type:mime});
    }

    // Show notification badge when chat is closed and there are new messages
    if (!chatOpen && messageCount > 0 && chatBadge) {
        chatBadge.style.display = 'flex';
        chatBadge.textContent = messageCount;
    }

    // Debug: Log that chatbot is loaded
    console.log('Chatbot loaded successfully!');
});
