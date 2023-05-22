import argparse
import gzip
import sys
import time
from huawei_lte_api.Client import Client
from huawei_lte_api.AuthorizedConnection import AuthorizedConnection
from binascii import unhexlify, hexlify
import threading
import huawei_lte_api.enums
import huawei_lte_api.exceptions
import random
import string


MAX_SMS_LEN = 160
SMS_HEADER = "1337"
MSG_ID_LEN = 3

def fetch_response(client, mifi_username, mifi_password):
    messages = {}
    while True:
        try:
            sms = client.sms.get_sms_list(1, box_type=huawei_lte_api.enums.sms.BoxTypeEnum.LOCAL_INBOX, unread_preferred=1)
            for message in sms['Messages']['Message']:
                if message['Content'].startswith('1337'):
                    #client.sms.set_read(message['Index'])
                    if len(message['Content']) < 13:
                        print('Invalid message length.')
                        continue

                    msg_id = message["Content"][4:7]
                    msg_counter = int(message["Content"][7:9], 16)
                    total_messages = int(message["Content"][9:11], 16)

                    if msg_id in messages:
                        messages[msg_id].append({
                            "counter": msg_counter,
                            "request": message['Content'][11:]
                        })
                    else:
                        messages[msg_id] = [{
                                "counter": msg_counter,
                                "request": message['Content'][11:]
                            }
                        ]

                    print(f"Message {msg_counter}/{total_messages} from {msg_id} received.")

                    client.sms.delete_sms(message['Index'])
                    #print(f"Total: {len(messages[msg_id])}/{total_messages} ({len(messages[msg_id]) / total_messages * 100:.2f}%)")

                    if len(messages[msg_id]) != total_messages:
                        continue

                    print("New response received.")
                    print()

                    sorted_messages = sorted(messages[msg_id], key=lambda x: x["counter"], reverse=False)
                    joined_requests = ''.join(message["request"] for message in sorted_messages)                    
                    
                    decompressed_request = gzip.decompress(unhexlify(joined_requests))
                    messages.__delitem__(msg_id)
                    print(decompressed_request.decode())
                    return
        except Exception as e:
            if type(e) in [huawei_lte_api.exceptions.ResponseErrorLoginRequiredException, huawei_lte_api.exceptions.ResponseErrorWrongSessionToken, huawei_lte_api.exceptions.LoginErrorAlreadyLoginException]:
                print('Reconnecting...')
                client.user.logout()
                client.user.login(mifi_username, mifi_password, force_new_login=True)
                heartbeat_thread = threading.Thread(target=fetch_response, args=(client,))
            print(f'Error: {e}')
        time.sleep(1)


def main(mifi_ip, mifi_username, mifi_password, whitelisted_number, verbose):
    connection = AuthorizedConnection(f'http://{mifi_ip}', mifi_username, mifi_password)
    client = Client(connection)
    while True:
        try:
            cmd = input("Enter command: ")
            if cmd == "exit":
                sys.exit(0)
            compressed_response = gzip.compress(cmd.encode())
            hex_response = hexlify(compressed_response).decode()
            msg_id = ''.join(random.choice(string.ascii_uppercase + string.digits) for _ in range(MSG_ID_LEN))

            max_sms_length = MAX_SMS_LEN - len(SMS_HEADER) - len(msg_id) - 2 - 2
            total_parts = (len(hex_response)) // max_sms_length + 1
            print(f"Sending {total_parts} messages. Total length: {len(hex_response)}")
            
            for i in range(0, len(hex_response), max_sms_length):
                client.sms.send_sms(whitelisted_number, f"{SMS_HEADER}{msg_id}{((i // max_sms_length)+1):02x}{total_parts:02x}{hex_response[i:i + max_sms_length]}")                        
                if verbose:
                    print(f"{SMS_HEADER}{msg_id}{((i // max_sms_length)+1):02x}{total_parts:02x}{hex_response[i:i + max_sms_length]}")
                time.sleep(2)
            print("Waiting for response...")
            time.sleep(2)
            fetch_response(client, mifi_username, mifi_password)                   

        except Exception as e:
            if type(e) in [huawei_lte_api.exceptions.ResponseErrorLoginRequiredException, huawei_lte_api.exceptions.ResponseErrorWrongSessionToken, huawei_lte_api.exceptions.LoginErrorAlreadyLoginException]:
                print('Reconnecting...')
                client.user.logout()
                client.user.login(mifi_username, mifi_password, force_new_login=True)
            print(f'Error: {e}')
        time.sleep(1)

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Huawei MiFi SMS relay.')
    parser.add_argument('--mifi-ip', required=True, help='Huawei MiFi IP address.')
    parser.add_argument('--mifi-username', required=True, help='Huawei MiFi username.')
    parser.add_argument('--mifi-password', required=True, help='Huawei MiFi password.')
    parser.add_argument('--number', required=True, help='Phone number.')
    parser.add_argument('-v', '--verbose', action='store_true', help='Verbose mode.')
    args = parser.parse_args()

    main(args.mifi_ip, args.mifi_username, args.mifi_password, args.number, args.verbose)
